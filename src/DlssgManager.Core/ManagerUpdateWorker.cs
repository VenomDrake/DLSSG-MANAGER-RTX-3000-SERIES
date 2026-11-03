using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace DlssgManager.Core;

/// <summary>Runs only in the current manager's verified helper copy, after explicit user consent.</summary>
public static class ManagerUpdateWorker
{
    public static async Task<ManagerUpdateResult> RunAsync(string planPath, string expectedPlanSha256,
        CancellationToken cancellationToken = default)
    {
        ManagerUpdatePlan? plan = null;
        FileStream? activity = null;
        try
        {
            plan = await ReadPlanAsync(planPath, expectedPlanSha256, true, cancellationToken).ConfigureAwait(false);
            var activityPath = Path.Combine(plan.OperationDirectory, ManagerUpdateCleanup.ActivityFileName);
            ManagerUpdateService.RejectReparsePath(activityPath);
            activity = new FileStream(activityPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await WaitForParentAsync(plan, cancellationToken).ConfigureAwait(false);
            await ValidatePayloadAsync(plan, cancellationToken).ConfigureAwait(false);
            if (!ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(plan.TargetExePath, cancellationToken).ConfigureAwait(false), plan.OriginalExeSha256))
                throw new InvalidDataException("The manager executable changed after this update was prepared.");
            EnsureNoRunningTarget(plan);
            bool reboot = false;
            if (plan.Mode == ManagerUpdateMode.Portable)
                await ApplyPortableAsync(plan, cancellationToken).ConfigureAwait(false);
            else
                reboot = await ApplyInstalledAsync(plan, cancellationToken).ConfigureAwait(false);
            VerifyInstalledVersion(plan, reboot);
            var result = new ManagerUpdateResult(true, reboot, "manager_update_completed", "", plan.FromVersion, plan.ToVersion);
            await WriteResultAsync(plan, result).ConfigureAwait(false);
            Relaunch(plan);
            return result;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            var result = new ManagerUpdateResult(false, false, "manager_update_failed", error.Message, plan?.FromVersion ?? "", plan?.ToVersion ?? "");
            if (plan is not null)
            {
                try { await WriteResultAsync(plan, result).ConfigureAwait(false); } catch (Exception writing) when (writing is IOException or UnauthorizedAccessException) { }
                try { if (File.Exists(plan.TargetExePath)) Relaunch(plan); } catch (Exception launching) when (launching is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { }
            }
            return result;
        }
        finally { activity?.Dispose(); }
    }

    internal static void VerifyInstalledVersion(ManagerUpdatePlan plan, bool rebootRequired)
    {
        var actual = ManagerUpdateService.ReadManagerVersion(plan.TargetExePath);
        if (!ManagerUpdateService.TryVersion(plan.ToVersion, out var expected)
            || actual != expected && !(rebootRequired && plan.Mode == ManagerUpdateMode.Installed
                && ManagerUpdateService.TryVersion(plan.FromVersion, out var previous) && actual == previous))
            throw new InvalidDataException("The installed manager version could not be confirmed.");
    }

    public static ManagerUpdateResult? ReadResult(string resultPath, string dataDirectory)
    {
        try
        {
            var path = Path.GetFullPath(resultPath);
            var updateRoot = Path.Combine(Path.GetFullPath(dataDirectory), "manager-updates");
            if (!ManagerUpdateService.IsWithin(updateRoot, path) || Path.GetFileName(path) != "update-result.json") return null;
            ManagerUpdateService.RejectReparsePath(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return null;
            var result = JsonSerializer.Deserialize<ManagerUpdateResult>(File.ReadAllBytes(path), ManagerUpdateService.Json);
            return result?.Code is "manager_update_completed" or "manager_update_failed" ? result : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    internal static async Task<ManagerUpdatePlan> ReadPlanAsync(string planPath, string expectedHash, bool verifyWorkerIdentity, CancellationToken token)
    {
        if (!ManagerUpdateService.IsHash(expectedHash)) throw new InvalidDataException("The update plan hash is invalid.");
        var path = Path.GetFullPath(planPath);
        ManagerUpdateService.RejectReparsePath(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 65536) throw new InvalidDataException("The update plan is missing or too large.");
        var raw = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (!ManagerUpdateService.HashEquals(Convert.ToHexString(SHA256.HashData(raw)), expectedHash))
            throw new InvalidDataException("The update plan changed after user confirmation.");
        var plan = JsonSerializer.Deserialize<ManagerUpdatePlan>(raw, ManagerUpdateService.Json)
            ?? throw new InvalidDataException("The update plan is empty.");
        ValidatePlanStructure(plan, path);
        if (verifyWorkerIdentity && !ManagerUpdateService.SamePath(Environment.ProcessPath ?? "", plan.WorkerExePath))
            throw new InvalidDataException("The update worker is not running from its verified copy.");
        if (!ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(plan.WorkerExePath, token).ConfigureAwait(false), plan.WorkerSha256))
            throw new InvalidDataException("The update worker copy changed.");
        return plan with { PlanSha256 = expectedHash };
    }

    internal static void ValidatePlanStructure(ManagerUpdatePlan plan, string actualPlanPath)
    {
        if (plan.SchemaVersion != 1 || !Enum.IsDefined(plan.Mode) || plan.ParentProcessId <= 0 || plan.ParentStartedUtcTicks <= 0
            || !ManagerUpdateService.TryVersion(plan.FromVersion, out var from) || !ManagerUpdateService.TryVersion(plan.ToVersion, out var to) || to <= from
            || !ManagerUpdateService.IsHash(plan.WorkerSha256) || !ManagerUpdateService.IsHash(plan.OriginalExeSha256)
            || !ManagerUpdateService.HashEquals(plan.WorkerSha256, plan.OriginalExeSha256))
            throw new InvalidDataException("Invalid manager update plan.");
        var data = Path.GetFullPath(plan.DataDirectory);
        var cache = Path.Combine(data, "manager-updates");
        var operation = Path.GetFullPath(plan.OperationDirectory);
        if (!ManagerUpdateService.SamePath(Path.GetDirectoryName(operation)!, cache)
            || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(operation), @"\Aupdate-[0-9a-f]{32}\z")
            || !ManagerUpdateService.SamePath(plan.PlanPath, actualPlanPath)
            || !ManagerUpdateService.SamePath(plan.PlanPath, Path.Combine(operation, "update-plan.json"))
            || !ManagerUpdateService.SamePath(plan.CandidateDirectory, Path.Combine(operation, "candidate"))
            || !ManagerUpdateService.SamePath(plan.WorkerExePath, Path.Combine(operation, "worker", ManagerUpdateService.ExeName))
            || !ManagerUpdateService.SamePath(plan.ResultPath, Path.Combine(operation, "update-result.json")))
            throw new InvalidDataException("The manager update paths are inconsistent.");
        foreach (var p in new[] { data, operation, plan.CandidateDirectory, plan.WorkerExePath, plan.TargetExePath }) ManagerUpdateService.RejectReparsePath(p);
        ManagerUpdateService.ValidateTargetPath(plan.TargetExePath);
        if (ManagerUpdateService.IsWithin(operation, plan.TargetExePath)) throw new InvalidDataException("The update cannot replace its worker or candidate.");
        ManagerUpdateService.ValidateLaunchArguments(plan.LaunchArguments, data);
        var expected = plan.Mode == ManagerUpdateMode.Portable ? ManagerUpdateService.OwnedFiles : [ManagerUpdateService.InstallerAssetName];
        if (plan.Files is null || plan.Files.Count != expected.Count || plan.Files.Any(f => f is null || !expected.Contains(f.Name, StringComparer.Ordinal)
            || f.Size < 0 || f.Size > ManagerUpdateService.MaximumAssetBytes || !ManagerUpdateService.IsHash(f.Sha256))
            || plan.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count)
            throw new InvalidDataException("Invalid manager update file inventory.");
    }

    internal static async Task ValidatePayloadAsync(ManagerUpdatePlan plan, CancellationToken token)
    {
        foreach (var file in plan.Files)
        {
            var path = Path.Combine(plan.CandidateDirectory, file.Name);
            ManagerUpdateService.RejectReparsePath(path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size
                || !ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(path, token).ConfigureAwait(false), file.Sha256))
                throw new InvalidDataException("The prepared manager update was modified or is incomplete.");
        }
        if (plan.Mode == ManagerUpdateMode.Portable)
        {
            if (!ManagerUpdateService.TryVersion(plan.ToVersion, out var version)
                || ManagerUpdateService.ReadManagerVersion(Path.Combine(plan.CandidateDirectory, ManagerUpdateService.ExeName)) != version)
                throw new InvalidDataException("The prepared executable version is invalid.");
        }
        else ManagerUpdateMsi.Validate(Path.Combine(plan.CandidateDirectory, ManagerUpdateService.InstallerAssetName), plan.ToVersion);
    }

    private static async Task WaitForParentAsync(ManagerUpdatePlan plan, CancellationToken token)
    {
        Process? parent;
        try { parent = Process.GetProcessById(plan.ParentProcessId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            if (parent.HasExited) return;
            long started; string path;
            try { started = parent.StartTime.ToUniversalTime().Ticks; path = parent.MainModule?.FileName ?? ""; }
            catch (InvalidOperationException) when (parent.HasExited) { return; }
            catch (System.ComponentModel.Win32Exception) when (parent.HasExited) { return; }
            if (parent.HasExited) return;
            if (started != plan.ParentStartedUtcTicks)
                throw new InvalidOperationException("The original manager process identity changed.");
            if (!ManagerUpdateService.SamePath(path, plan.TargetExePath))
                throw new InvalidOperationException("The original manager process path changed.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
    }

    private static void EnsureNoRunningTarget(ManagerUpdatePlan plan)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(plan.TargetExePath)))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (!process.HasExited && ManagerUpdateService.SamePath(process.MainModule?.FileName ?? "", plan.TargetExePath))
                        throw new InvalidOperationException("Another manager window is still using this executable. Close it before updating.");
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception)
                { throw new InvalidOperationException("Another manager process could not be checked. Close other manager windows before updating."); }
            }
        }
    }

    /// <summary>All replacements are same-volume. Original owned files are retained in the operation backup.</summary>
    internal static async Task ApplyPortableAsync(ManagerUpdatePlan plan, CancellationToken token, Action<int>? faultInjection = null)
    {
        var targetDir = Path.GetDirectoryName(plan.TargetExePath)!;
        var stage = Path.Combine(targetDir, ".manager-update-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(plan.OperationDirectory, "backup");
        ManagerUpdateService.EnsureSpace(targetDir, plan.Files.Sum(f => f.Size) * 3 + 16L * 1024 * 1024);
        ManagerUpdateService.EnsureSafeDirectory(stage);
        ManagerUpdateService.EnsureSafeDirectory(backup);
        var originals = new List<(string Name, bool Exists, string? Hash)>();
        var applied = new List<string>();
        try
        {
            // Snapshot every owned file before the first target mutation.
            foreach (var file in plan.Files)
            {
                token.ThrowIfCancellationRequested();
                var target = Path.Combine(targetDir, file.Name);
                ManagerUpdateService.RejectReparsePath(target);
                if (Directory.Exists(target)) throw new IOException("An update file name is occupied by a directory.");
                var exists = File.Exists(target);
                string? hash = null;
                if (exists)
                {
                    hash = await ManagerUpdateService.HashAsync(target, token).ConfigureAwait(false);
                    await ManagerUpdateService.CopyAsync(target, Path.Combine(backup, file.Name), token).ConfigureAwait(false);
                    if (!ManagerUpdateService.HashEquals(hash, await ManagerUpdateService.HashAsync(Path.Combine(backup, file.Name), token).ConfigureAwait(false)))
                        throw new IOException("A manager file changed while making its backup.");
                }
                originals.Add((file.Name, exists, hash));
                await ManagerUpdateService.CopyAsync(Path.Combine(plan.CandidateDirectory, file.Name), Path.Combine(stage, file.Name), token).ConfigureAwait(false);
                if (!ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(Path.Combine(stage, file.Name), token).ConfigureAwait(false), file.Sha256))
                    throw new InvalidDataException("Update staging verification failed.");
            }
            token.ThrowIfCancellationRequested();
            foreach (var file in plan.Files.OrderBy(f => f.Name == ManagerUpdateService.ExeName ? 1 : 0))
            {
                var destination = Path.Combine(targetDir, file.Name);
                ManagerUpdateService.RejectReparsePath(destination);
                var original = originals.Single(o => o.Name == file.Name);
                if (File.Exists(destination) != original.Exists || original.Exists
                    && !ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(destination, CancellationToken.None).ConfigureAwait(false), original.Hash!))
                    throw new IOException("A manager file changed while preparing the update.");
                if (original.Exists) File.Replace(Path.Combine(stage, file.Name), destination, null);
                else File.Move(Path.Combine(stage, file.Name), destination);
                applied.Add(file.Name);
                faultInjection?.Invoke(applied.Count);
            }
            foreach (var file in plan.Files)
                if (!ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(Path.Combine(targetDir, file.Name), CancellationToken.None).ConfigureAwait(false), file.Sha256))
                    throw new InvalidDataException("Manager update verification failed after replacement.");
        }
        catch (Exception operationError)
        {
            try
            {
                foreach (var name in applied.AsEnumerable().Reverse())
                {
                    var target = Path.Combine(targetDir, name);
                    ManagerUpdateService.RejectReparsePath(target);
                    var original = originals.Single(o => o.Name == name);
                    if (original.Exists)
                    {
                        if (!ManagerUpdateService.HashEquals(await ManagerUpdateService.HashAsync(Path.Combine(backup, name), CancellationToken.None).ConfigureAwait(false), original.Hash!))
                            throw new IOException("An original manager backup changed before recovery.");
                        var restore = Path.Combine(stage, "restore-" + name);
                        await ManagerUpdateService.CopyAsync(Path.Combine(backup, name), restore, CancellationToken.None).ConfigureAwait(false);
                        if (File.Exists(target)) File.Replace(restore, target, null); else File.Move(restore, target);
                    }
                    else File.Delete(target);
                }
            }
            catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
            { throw new IOException("The manager update failed and its backup needs manual recovery: " + backup, new AggregateException(operationError, rollbackError)); }
            throw;
        }
        finally
        {
            try { if (ManagerUpdateService.IsWithin(targetDir, stage)) { ManagerUpdateService.RejectReparsePath(stage); Directory.Delete(stage, true); } }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<bool> ApplyInstalledAsync(ManagerUpdatePlan plan, CancellationToken token)
    {
        var installer = Path.Combine(plan.CandidateDirectory, ManagerUpdateService.InstallerAssetName);
        ManagerUpdateMsi.Validate(installer, plan.ToVersion);
        using var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"))
        { UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in new[] { "/i", installer, "/passive", "/norestart", "APPFOLDER=" + Path.GetDirectoryName(plan.TargetExePath) }) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new InvalidOperationException("Windows Installer could not be started.");
        // Once Windows Installer starts it owns its transaction. Cancellation must not terminate it.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        if (process.ExitCode is not (0 or 3010)) throw new IOException("Windows Installer did not complete the manager update (" + process.ExitCode + ").");
        return process.ExitCode == 3010;
    }

    private static async Task WriteResultAsync(ManagerUpdatePlan plan, ManagerUpdateResult result)
    {
        ManagerUpdateService.RejectReparsePath(plan.OperationDirectory);
        var stage = Path.Combine(plan.OperationDirectory, "update-result.tmp");
        ManagerUpdateService.RejectReparsePath(stage);
        ManagerUpdateService.RejectReparsePath(plan.ResultPath);
        await using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(result, ManagerUpdateService.Json)).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        File.Move(stage, plan.ResultPath, true);
    }
    private static void Relaunch(ManagerUpdatePlan plan)
    {
        ManagerUpdateService.ValidateTargetPath(plan.TargetExePath);
        var start = new ProcessStartInfo(plan.TargetExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(plan.TargetExePath) };
        foreach (var argument in plan.LaunchArguments) start.ArgumentList.Add(argument);
        if (!plan.LaunchArguments.Contains("--data-dir")) { start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(plan.DataDirectory); }
        start.ArgumentList.Add("--manager-update-result"); start.ArgumentList.Add(plan.ResultPath);
        if (Process.Start(start) is null) throw new InvalidOperationException("The manager could not be restarted.");
    }
}

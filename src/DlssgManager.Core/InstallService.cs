using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DlssgManager.Core;

public sealed record InstalledFile(string Path, long Size, string Sha256);

public sealed record InstallationRecord
{
    public ModProvider Provider { get; init; } = ModProvider.Sm86;
    public string Variant { get; init; } = "full";
    public bool ExternalOptiScaler { get; init; }
    public string? ExternalProxy { get; init; }
    public string? ExternalProxySha256 { get; init; }
    public long ExternalProxySize { get; init; }
    public string? ExternalLoaderBackupDirectory { get; init; }
    public string? OriginalFgInput { get; init; }
    public string? OriginalFgReplacement { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string Version { get; init; } = "";
    public long ReleaseId { get; init; }
    public long AssetId { get; init; }
    public string PackageSha256 { get; init; } = "";
    public string GameDirectory { get; init; } = "";
    public string RenderingExe { get; init; } = "";
    public string Proxy { get; init; } = "version.dll";
    public string State { get; init; } = "Installed";
    public IReadOnlyList<InstalledFile> Files { get; init; } = [];
    public string BackupDirectory { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
    public ModOptions Options { get; init; } = new();
}

public sealed record InstallOutcome(InstallationRecord? Record, string Message, bool Changed);

/// <summary>
/// Installs only the proxy, INI and runtime directory. Original files are snapshotted and
/// verified before mutation. A write-ahead journal makes an interrupted operation recoverable.
/// No downloaded executable, PowerShell script or DLL is executed by this service.
/// </summary>
public sealed partial class InstallService
{
    public const string RecordName = ".dlssg-manager.json";
    private const string JournalName = ".dlssg-manager.transaction.json";
    private const string BackupRoot = ".dlssg-manager-backups";
    private static readonly string[] ProxyNames = ["version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll"];
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public async Task<InstallOutcome> InstallAsync(GameScan scan, ReleasePackage package, ModOptions options,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!scan.CanInstall || scan.RenderingDirectory is null || scan.RenderingExe is null)
            throw new InvalidOperationException("Preliminary checks must pass before installation.");
        if (!package.SupportedLayout || package.Checks.Any(c => c.Level == CheckLevel.Blocked))
            throw new InvalidOperationException("This release package is not supported by this version of the manager.");
        ValidateOptions(options);
        if (package.Provider != options.Provider || scan.Provider != options.Provider)
            throw new InvalidOperationException("The selected provider changed after analysis. Scan the game again.");
        var directory = ValidateGameDirectory(scan.RenderingDirectory);
        var exe = Path.GetFullPath(scan.RenderingExe);
        if (!SamePath(Path.GetDirectoryName(exe)!, directory) || !File.Exists(exe) || !IsX64Executable(exe))
            throw new InvalidOperationException("Select the game's actual x64 rendering executable.");
        if (scan.RuntimePath is not null)
        {
            EnsureNoReparse(scan.RuntimePath);
            if (!File.Exists(scan.RuntimePath) || !IsX64Executable(scan.RuntimePath) || options.Provider == ModProvider.Sm86 && FileVersionInfo.GetVersionInfo(scan.RuntimePath).FileMajorPart != 310)
                throw new InvalidOperationException("The game's DLSS-G runtime changed after analysis. Scan the game again.");
        }
        using var operationLock = AcquireLock(directory);
        await RecoverPendingAsync(directory, cancellationToken);
        EnsureGameClosed(exe, directory);
        var previous = await ReadRecordAsync(directory, cancellationToken);
        if (previous is not null)
        {
            await VerifyInstalledFilesAsync(directory, previous, cancellationToken);
            if (previous.Provider == package.Provider && CompareVersions(previous.Version, package.Release.Version) > 0)
                throw new InvalidOperationException("A newer version is installed. Restore its backup before downgrading.");
        }
        await ValidateCollisionsAsync(directory, scan, package, previous, cancellationToken);
        var samePackage = previous is not null && previous.State == "Installed" && previous.Provider == package.Provider && previous.Variant == package.Variant &&
            previous.ReleaseId == package.Release.ReleaseId && previous.AssetId == package.Release.AssetId &&
            string.Equals(previous.PackageSha256, package.Release.Sha256, StringComparison.OrdinalIgnoreCase);
        if (samePackage && previous!.Options == options &&
            (options.Provider != ModProvider.Anr || await AnrConfigurationMatchesAsync(directory, options, cancellationToken)))
            return new(previous, "already_current", false);

        var stageRelative = ".dlssg-manager-stage-" + Guid.NewGuid().ToString("N");
        var stage = Child(directory, stageRelative);
        Directory.CreateDirectory(stage);
        try
        {
            progress?.Report(new("install_staging", 5));
            var stagedFiles = package.Provider == ModProvider.Anr
                ? await StageAnrPackageAsync(package, stage, options, directory, previous, cancellationToken)
                : await StageSm86PackageAsync(package, stage, options, directory, previous, cancellationToken);
            var roots = InstallationRoots(previous, options.Proxy, package.Provider)
                .Union(ProxyNames.Where(p => File.Exists(Child(directory, p))), StringComparer.OrdinalIgnoreCase).ToArray();
            var packageBytes = stagedFiles.Sum(f => f.Size);
            EnsureDiskSpace(directory, checked(packageBytes + EstimateRootsBytes(directory, roots) * 2 + 16L * 1024 * 1024));
            EnsureGameClosed(exe, directory);
            progress?.Report(new("install_backup", 40));
            var snapshot = await CreateSnapshotAsync(directory, roots, cancellationToken);
            var record = new InstallationRecord
            {
                Version = package.Release.Version, ReleaseId = package.Release.ReleaseId,
                AssetId = package.Release.AssetId, PackageSha256 = package.Release.Sha256,
                GameDirectory = directory, RenderingExe = exe, Proxy = options.Proxy,
                State = "Installed", Files = stagedFiles, BackupDirectory = snapshot.RelativeDirectory,
                Options = options, Provider = package.Provider, Variant = package.Variant,
                ExternalOptiScaler = package.Provider == ModProvider.Anr && package.Variant == "addon" || previous?.ExternalOptiScaler == true,
                ExternalProxy = previous?.ExternalProxy, ExternalProxySha256 = previous?.ExternalProxySha256,
                ExternalProxySize = previous?.ExternalProxySize ?? 0,
                ExternalLoaderBackupDirectory = previous?.ExternalLoaderBackupDirectory,
                OriginalFgInput = previous?.OriginalFgInput, OriginalFgReplacement = previous?.OriginalFgReplacement
            };
            if (package.Provider == ModProvider.Anr && package.Variant == "addon" && previous?.ExternalOptiScaler != true)
                record = await BindExternalOptiScalerAsync(directory, record, cancellationToken);
            if (package.Provider == ModProvider.Sm86 && previous is { Provider: ModProvider.Anr, ExternalOptiScaler: true })
                record = record with { ExternalLoaderBackupDirectory = snapshot.RelativeDirectory };
            await WriteJsonAsync(Child(stage, RecordName), record, cancellationToken);
            await VerifySnapshotSourceAsync(directory, snapshot, cancellationToken);
            if (previous is not null) await VerifyInstalledFilesAsync(directory, previous, cancellationToken);
            await ValidateCollisionsAsync(directory, scan, package, previous, cancellationToken);
            await WriteJournalAsync(directory, snapshot, stageRelative, cancellationToken, exe);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureGameClosed(exe, directory);
                // Deactivate every known loader before replacing data. A game started during the
                // transaction can only take its original path, never load a half-replaced mod.
                foreach (var proxy in roots.Where(IsProxyFile)) DeletePlannedRoot(directory, proxy);
                progress?.Report(new("install_commit", 75));
                foreach (var root in roots.Where(r => !ProxyNames.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    DeletePlannedRoot(directory, root);
                foreach (var dataRoot in roots.Where(r => !IsProxyFile(r) && !SameRelative(r, RecordName)))
                {
                    var stagedRoot = Child(stage, dataRoot);
                    if (!Exists(stagedRoot)) continue;
                    var destination = Child(directory, dataRoot);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (Directory.Exists(stagedRoot)) Directory.Move(stagedRoot, destination); else File.Move(stagedRoot, destination);
                }
                EnsureGameClosed(exe, directory);
                File.Move(Child(stage, options.Proxy), Child(directory, options.Proxy));
                File.Move(Child(stage, RecordName), Child(directory, RecordName));
                await VerifyInstalledFilesAsync(directory, record, CancellationToken.None);
                File.Delete(Child(directory, JournalName));
                progress?.Report(new("install_done", 100));
                return new(record, samePackage ? "settings_updated" : "install_done", true);
            }
            catch (Exception operationError)
            {
                await RollBackOrThrowAsync(directory, snapshot, operationError);
                throw;
            }
        }
        finally { CleanupStage(directory, stageRelative); }
    }

    public async Task<InstallOutcome> RestoreAsync(string renderingDirectory,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = ValidateGameDirectory(renderingDirectory);
        using var operationLock = AcquireLock(directory);
        await RecoverPendingAsync(directory, cancellationToken);
        var record = await ReadRecordAsync(directory, cancellationToken)
            ?? throw new InvalidOperationException("There is no managed installation to restore.");
        EnsureGameClosed(record.RenderingExe, directory);
        await VerifyInstalledFilesAsync(directory, record, cancellationToken);
        var original = await LoadSnapshotAsync(directory, record.BackupDirectory, cancellationToken);
        var roots = original.Roots.Select(r => r.Path).Union(InstallationRoots(record, record.Proxy, record.Provider), StringComparer.OrdinalIgnoreCase).ToArray();
        ValidateUnexpectedProxyRoots(directory, roots, record);
        progress?.Report(new("restore_backup", 10));
        EnsureDiskSpace(directory, checked(EstimateRootsBytes(directory, roots) * 2 + original.Files.Sum(f => f.Size) + 16L * 1024 * 1024));
        var undo = await CreateSnapshotAsync(directory, roots, cancellationToken);
        await VerifySnapshotSourceAsync(directory, undo, cancellationToken);
        await WriteJournalAsync(directory, undo, null, cancellationToken, record.RenderingExe);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGameClosed(record.RenderingExe, directory);
            await ApplySnapshotAsync(directory, original, undo.RelativeDirectory, CancellationToken.None, roots);
            File.Delete(Child(directory, JournalName));
            var restored = await ReadRecordAsync(directory, CancellationToken.None);
            progress?.Report(new("restore_done", 100));
            return new(restored, "restore_done", true);
        }
        catch (Exception operationError)
        {
            await RollBackOrThrowAsync(directory, undo, operationError);
            throw;
        }
    }

    public async Task<InstallOutcome> DisableAsync(string renderingDirectory, CancellationToken cancellationToken = default)
    {
        var directory = ValidateGameDirectory(renderingDirectory);
        using var operationLock = AcquireLock(directory);
        await RecoverPendingAsync(directory, cancellationToken);
        var record = await ReadRecordAsync(directory, cancellationToken)
            ?? throw new InvalidOperationException("There is no managed installation to disable.");
        if (record.State == "Disabled") return new(record, "already_disabled", false);
        EnsureGameClosed(record.RenderingExe, directory);
        await VerifyInstalledFilesAsync(directory, record, cancellationToken);
        if (record.Provider == ModProvider.Anr && record.ExternalOptiScaler)
            return await DisableAnrAddonAsync(directory, record, cancellationToken);
        var disabled = record.Proxy + ".disabled";
        if (Exists(Child(directory, disabled))) throw new IOException("A disabled proxy already exists. No file was overwritten.");
        var snapshot = await CreateSnapshotAsync(directory, [record.Proxy, disabled, RecordName], cancellationToken);
        await VerifySnapshotSourceAsync(directory, snapshot, cancellationToken);
        await WriteJournalAsync(directory, snapshot, null, cancellationToken, record.RenderingExe);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGameClosed(record.RenderingExe, directory);
            File.Move(Child(directory, record.Proxy), Child(directory, disabled));
            var updated = record with
            {
                State = "Disabled",
                Files = record.Files.Select(f => SameRelative(f.Path, record.Proxy) ? f with { Path = disabled } : f).ToArray()
            };
            await WriteJsonAtomicAsync(Child(directory, RecordName), updated, CancellationToken.None);
            File.Delete(Child(directory, JournalName));
            return new(updated, "disable_done", true);
        }
        catch (Exception operationError)
        {
            await RollBackOrThrowAsync(directory, snapshot, operationError);
            throw;
        }
    }

    public async Task<InstallationRecord?> ReadRecordAsync(string renderingDirectory, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(renderingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Child(directory, RecordName);
        if (!File.Exists(path)) return null;
        EnsureNoReparse(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("The installation record is too large.");
        await using var stream = File.OpenRead(path);
        InstallationRecord record;
        try
        {
            record = await JsonSerializer.DeserializeAsync<InstallationRecord>(stream, Json, cancellationToken)
                ?? throw new InvalidDataException("The installation record is empty.");
        }
        catch (JsonException error) { throw new InvalidDataException("The installation record contains invalid JSON.", error); }
        if (record.Files is null || record.Options is null || string.IsNullOrWhiteSpace(record.GameDirectory) || string.IsNullOrWhiteSpace(record.RenderingExe) ||
            string.IsNullOrWhiteSpace(record.BackupDirectory) || record.SchemaVersion != 1 || !SamePath(record.GameDirectory, directory) ||
            !ProxyNames.Contains(record.Proxy, StringComparer.OrdinalIgnoreCase) ||
            record.State is not ("Installed" or "Disabled") || !IsBackupRelative(record.BackupDirectory) ||
            record.Files.Count is < 3 or > 4096 || string.IsNullOrWhiteSpace(record.Version) || !Enum.IsDefined(record.Provider) ||
            record.Variant is not ("full" or "addon") || record.Options.Provider != record.Provider)
            throw new InvalidDataException("The installation record is not valid for this game.");
        try { ValidateOptions(record.Options); }
        catch (ArgumentException error) { throw new InvalidDataException("The installation record contains invalid mod options.", error); }
        if (!SameRelative(record.Options.Proxy, record.Proxy)) throw new InvalidDataException("The recorded mod options refer to a different proxy.");
        if (!SamePath(Path.GetDirectoryName(Path.GetFullPath(record.RenderingExe))!, directory))
            throw new InvalidDataException("The recorded executable is outside the game directory.");
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in record.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path) || !IsSha256(file.Sha256))
                throw new InvalidDataException("The installed file inventory is invalid.");
            ValidateInstalledRelative(file.Path, record.Proxy);
            if (!duplicates.Add(Normalize(file.Path)) || file.Size < 0 || !IsSha256(file.Sha256))
                throw new InvalidDataException("The installed file inventory is invalid.");
            _ = Child(directory, file.Path);
        }
        var actualProxy = record.State == "Disabled" && !(record.Provider == ModProvider.Anr && record.ExternalOptiScaler) ? record.Proxy + ".disabled" : record.Proxy;
        ValidateExternalOptiScalerRecord(record);
        if (!record.Files.Any(f => SameRelative(f.Path, actualProxy)) ||
            (record.Provider == ModProvider.Sm86
                ? !record.Files.Any(f => SameRelative(f.Path, "dlssg_sm86.ini")) || !record.Files.Any(f => SameRelative(f.Path, "dlssg_sm86/backend/sm86_backend.dll"))
                : !record.Files.Any(f => SameRelative(f.Path, "OptiScaler.ini")) || !record.Files.Any(f => SameRelative(f.Path, AnrProviderRoot + "/dlssg/nvngx_dlssg.dll")) ||
                  !record.Files.Any(f => SameRelative(f.Path, AnrIniPath)) || !record.Files.Any(f => SameRelative(f.Path, AnrProviderRoot + "/dlssg/kernels/pack.nrx"))))
            throw new InvalidDataException("The installation record is missing required files.");
        return record;
    }

    public static void ValidateOptions(ModOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Provider)) throw new ArgumentException("Unknown frame generation provider.", nameof(options));
        if (options.Provider == ModProvider.Anr)
        {
            ValidateAnrOptions(options);
            return;
        }
        if (options.Engine is not ("DLSS" or "XeSS") || options.Multiplier is < 2 or > 6 ||
            options.Engine == "XeSS" && options.Multiplier > 4 || options.Optimization is < 0 or > 3 ||
            options.Preset is not ("A" or "B" or "C") ||
            options.SuperResolution is not ("Off" or "M-INT8" or "L-INT8" or "M-FP16" or "L-FP16") ||
            options.FlowResolution is not ("Coarse" or "Middle" or "Fine" or "CoarseAt4K") ||
            options.UseHudless is not ("Auto" or "0" or "1") || options.LoggingLevel is < 0 or > 3 ||
            !ProxyNames.Contains(options.Proxy, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("One or more mod options are invalid or unsupported.", nameof(options));
    }

    public static string ConfigureIni(string source, ModOptions options)
    {
        ValidateOptions(options);
        var output = source;
        foreach (var (section, key, value) in new (string, string, string)[]
        {
            ("General", "Enabled", "1"), ("FrameGeneration", "Engine", options.Engine),
            ("FrameGeneration", "MaxGeneratedFrames", (options.Multiplier - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("DLSSG", "Optimized", options.Optimization.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("DLSSG", "Preset", options.Preset), ("XeSSFG", "FlowResolution", options.FlowResolution),
            ("XeSSFG", "UseHudless", options.UseHudless),
            ("Logging", "Level", options.LoggingLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Logging", "FrameTiming", options.FrameTiming ? "1" : "0"),
            ("Logging", "Directory", "dlssg_sm86\\logs"), ("Runtime", "Mode", "Bundled"),
            ("SuperResolution", "Mode", options.SuperResolution)
        }) output = SetIniValue(output, section, key, value);
        return output;
    }

    private static string SetIniValue(string source, string section, string key, string value)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n').ToList();
        var sectionStart = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (sectionStart < 0) { lines.Add(""); lines.Add("[" + section + "]"); lines.Add(key + "=" + value); return string.Join("\n", lines); }
        var sectionEnd = lines.FindIndex(sectionStart + 1, l => l.TrimStart().StartsWith('['));
        if (sectionEnd < 0) sectionEnd = lines.Count;
        var matches = new List<int>();
        for (var index = sectionStart + 1; index < sectionEnd; index++)
        {
            var line = lines[index].TrimStart();
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals > 0 && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) matches.Add(index);
        }
        if (matches.Count > 1) throw new InvalidDataException($"The package INI has a duplicate {section}/{key} setting.");
        if (matches.Count == 1) lines[matches[0]] = key + "=" + value;
        else lines.Insert(sectionEnd, key + "=" + value);
        return string.Join("\n", lines);
    }

    private static async Task<List<InstalledFile>> StagePackageAsync(ReleasePackage package, string stage, ModOptions options, CancellationToken token)
    {
        EnsureNoReparse(package.Directory);
        var files = package.Files.Where(f => Normalize(f.Path).StartsWith("dlssg_sm86/", StringComparison.OrdinalIgnoreCase)).ToList();
        var proxySource = options.Proxy == "version.dll" ? "version.dll" : "alternatives/" + options.Proxy;
        var proxy = package.Files.SingleOrDefault(f => SameRelative(f.Path, proxySource))
            ?? throw new InvalidDataException("The selected proxy is missing from the verified package.");
        var ini = package.Files.SingleOrDefault(f => SameRelative(f.Path, "dlssg_sm86.ini"))
            ?? throw new InvalidDataException("The package configuration is missing.");
        if (files.Count == 0 || files.Count > 4096) throw new InvalidDataException("The package runtime inventory is invalid.");
        foreach (var required in new[] { "dlssg_sm86/backend/sm86_backend.dll", "dlssg_sm86/kernels/fg_kernels.dll", "dlssg_sm86/runtime/nvngx_dlssg.dll" })
            if (!files.Any(f => SameRelative(f.Path, required))) throw new InvalidDataException("A required runtime file is missing: " + required);
        var sourceFiles = files.Append(proxy).Append(ini).ToArray();
        if (sourceFiles.Select(f => Normalize(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceFiles.Length)
            throw new InvalidDataException("The package inventory contains duplicate paths.");
        var installed = new List<InstalledFile>();
        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();
            var source = Child(package.Directory, file.Path);
            await VerifyFileAsync(source, file.Size, file.Sha256, token);
            var relative = SameRelative(file.Path, proxySource) ? options.Proxy : Normalize(file.Path);
            ValidateInstalledRelative(relative, options.Proxy);
            var target = Child(stage, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (SameRelative(relative, "dlssg_sm86.ini"))
            {
                var text = await File.ReadAllTextAsync(source, token);
                await File.WriteAllTextAsync(target, ConfigureIni(text, options), new UTF8Encoding(false), token);
            }
            else
            {
                await CopyFileAsync(source, target, token);
                await VerifyFileAsync(target, file.Size, file.Sha256, token);
            }
            installed.Add(await InventoryFileAsync(stage, relative, token));
        }
        return installed;
    }

    private async Task ValidateCollisionsAsync(string directory, GameScan scan, ReleasePackage package,
        InstallationRecord? previous, CancellationToken token)
    {
        if (package.Provider == ModProvider.Anr || previous?.Provider == ModProvider.Anr || previous?.ExternalOptiScaler == true)
        {
            await ValidateProviderCollisionsAsync(directory, scan, package, previous, token);
            return;
        }
        if (scan.ModState == ModState.UnknownConflict) throw new InvalidOperationException("An unknown mod or proxy is already installed.");
        var allowed = previous is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) :
            new HashSet<string>([previous.State == "Disabled" ? previous.Proxy + ".disabled" : previous.Proxy], StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in ProxyNames)
        {
            var path = Child(directory, proxy);
            if (!Exists(path)) continue;
            if (allowed.Contains(proxy)) continue;
            if (previous is not null || scan.ModState != ModState.RecognizedManual)
                throw new InvalidOperationException("An unrecognized proxy exists: " + proxy);
            var source = package.Files.SingleOrDefault(f => SameRelative(f.Path, proxy == "version.dll" ? proxy : "alternatives/" + proxy));
            if (source is null) throw new InvalidOperationException("The existing proxy cannot be identified: " + proxy);
            await VerifyFileAsync(path, source.Size, source.Sha256, token);
            allowed.Add(proxy);
        }
        if (allowed.Count > 1) throw new InvalidOperationException("More than one proxy is present. Keep one proxy before continuing.");
        if (previous is null && (Exists(Child(directory, "dlssg_sm86")) || Exists(Child(directory, "dlssg_sm86.ini"))))
        {
            if (scan.ModState != ModState.RecognizedManual || allowed.Count != 1 || !File.Exists(Child(directory, "dlssg_sm86.ini")))
                throw new InvalidOperationException("Existing mod files were not recognized and will not be overwritten.");
            foreach (var file in package.Files.Where(f => Normalize(f.Path).StartsWith("dlssg_sm86/", StringComparison.OrdinalIgnoreCase)))
                await VerifyFileAsync(Child(directory, file.Path), file.Size, file.Sha256, token);
        }
    }

    private static string[] InstallationRoots(InstallationRecord? previous, string newProxy, ModProvider provider = ModProvider.Sm86)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dlssg_sm86", "dlssg_sm86.ini", RecordName, newProxy };
        if (provider == ModProvider.Anr || previous?.Provider == ModProvider.Anr || previous?.ExternalOptiScaler == true)
        {
            roots.Add(AnrProviderRoot); roots.Add("OptiScaler.ini");
        }
        if (previous is not null) roots.Add(previous.State == "Disabled" && !(previous.Provider == ModProvider.Anr && previous.ExternalOptiScaler) ? previous.Proxy + ".disabled" : previous.Proxy);
        // Recognized manual installations may have selected a different proxy.
        return roots.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task VerifyInstalledFilesAsync(string directory, InstallationRecord record, CancellationToken token)
    {
        foreach (var file in record.Files)
        {
            if (IsMutableConfiguration(file.Path))
            {
                var path = Child(directory, file.Path);
                if (!File.Exists(path)) throw new IOException("The installed configuration is missing.");
                EnsureNoReparse(path); // Settings may be edited, but are still preserved in every snapshot.
                continue;
            }
            await VerifyFileAsync(Child(directory, file.Path), file.Size, file.Sha256, token);
        }
    }

    private sealed record SnapshotRoot(string Path, bool Existed, bool Directory);
    private sealed record SnapshotFile(string Path, long Size, string Sha256);
    private sealed record Snapshot(string RelativeDirectory, IReadOnlyList<SnapshotRoot> Roots, IReadOnlyList<SnapshotFile> Files,
        IReadOnlyList<string> Directories);
    private sealed record Journal(int SchemaVersion, string GameDirectory, string SnapshotDirectory, string? StageDirectory,
        string? RenderingExe = null);

    private static async Task<Snapshot> CreateSnapshotAsync(string directory, IEnumerable<string> rootPaths, CancellationToken token)
    {
        var relative = BackupRoot + "/" + Guid.NewGuid().ToString("N");
        var backup = Child(directory, relative);
        Directory.CreateDirectory(backup);
        var roots = new List<SnapshotRoot>();
        var inventory = new List<SnapshotFile>();
        var directories = new List<string>();
        foreach (var root in rootPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            ValidatePlannedRoot(root);
            var source = Child(directory, root);
            if (!Exists(source)) { roots.Add(new(root, false, false)); continue; }
            EnsureNoReparse(source);
            var isDirectory = Directory.Exists(source);
            roots.Add(new(root, true, isDirectory));
            if (isDirectory)
            {
                Directory.CreateDirectory(Child(backup, "original/" + root));
                foreach (var subdirectory in EnumerateTreeDirectories(source))
                {
                    var subRelative = root + "/" + Normalize(Path.GetRelativePath(source, subdirectory));
                    directories.Add(subRelative);
                    Directory.CreateDirectory(Child(backup, "original/" + subRelative));
                }
                foreach (var file in EnumerateTreeFiles(source))
                {
                    var fileRelative = root + "/" + Normalize(Path.GetRelativePath(source, file));
                    var target = Child(backup, "original/" + fileRelative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await CopyFileAsync(file, target, token);
                    var item = await InventoryFileAsync(directory, fileRelative, token);
                    await VerifyFileAsync(target, item.Size, item.Sha256, token);
                    inventory.Add(new(fileRelative, item.Size, item.Sha256));
                }
            }
            else
            {
                var target = Child(backup, "original/" + root);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyFileAsync(source, target, token);
                var item = await InventoryFileAsync(directory, root, token);
                await VerifyFileAsync(target, item.Size, item.Sha256, token);
                inventory.Add(new(root, item.Size, item.Sha256));
            }
        }
        var snapshot = new Snapshot(relative, roots, inventory, directories);
        await WriteJsonAsync(Child(backup, "snapshot.json"), snapshot, token);
        return snapshot;
    }

    private static async Task<Snapshot> LoadSnapshotAsync(string directory, string relative, CancellationToken token)
    {
        if (!IsBackupRelative(relative)) throw new InvalidDataException("The backup path is invalid.");
        var backup = Child(directory, relative);
        EnsureNoReparse(backup);
        var path = Child(backup, "snapshot.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("The backup manifest is missing or too large.");
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, Json, token) ?? throw new InvalidDataException("The backup manifest is empty.");
        if (snapshot.Roots is null || snapshot.Files is null || !SameRelative(snapshot.RelativeDirectory, relative) ||
            snapshot.Roots.Count is < 1 or > 12 || snapshot.Files.Count > 20000 ||
            snapshot.Directories is null || snapshot.Directories.Count > 20000)
            throw new InvalidDataException("The backup manifest is invalid.");
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in snapshot.Roots)
        {
            ValidatePlannedRoot(root.Path);
            if (!roots.Add(Normalize(root.Path))) throw new InvalidDataException("The backup contains duplicate roots.");
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in snapshot.Files)
        {
            var normalized = Normalize(file.Path);
            if (!files.Add(normalized) || !snapshot.Roots.Any(r => r.Existed &&
                    (SameRelative(r.Path, normalized) || r.Directory && normalized.StartsWith(Normalize(r.Path) + "/", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("The backup file inventory is invalid.");
            await VerifyFileAsync(Child(backup, "original/" + normalized), file.Size, file.Sha256, token);
        }
        foreach (var subdirectory in snapshot.Directories)
        {
            if (!snapshot.Roots.Any(r => r.Existed && r.Directory && Normalize(subdirectory).StartsWith(Normalize(r.Path) + "/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The backup directory inventory is invalid.");
            if (!Directory.Exists(Child(backup, "original/" + subdirectory)))
                throw new InvalidDataException("A backup directory is missing.");
        }
        return snapshot;
    }

    private static async Task VerifySnapshotSourceAsync(string directory, Snapshot snapshot, CancellationToken token)
    {
        foreach (var root in snapshot.Roots)
        {
            var source = Child(directory, root.Path);
            if (Exists(source) != root.Existed || root.Existed && Directory.Exists(source) != root.Directory)
                throw new IOException("Game files changed during backup. No installation was committed; scan the game again.");
            if (root.Existed && root.Directory)
            {
                var current = EnumerateTreeFiles(source).Select(p => Normalize(root.Path + "/" + Path.GetRelativePath(source, p)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var expected = snapshot.Files.Where(f => Normalize(f.Path).StartsWith(Normalize(root.Path) + "/", StringComparison.OrdinalIgnoreCase))
                    .Select(f => Normalize(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!current.SetEquals(expected)) throw new IOException("Game files changed during backup. Scan the game again.");
                var directories = EnumerateTreeDirectories(source).Select(p => Normalize(root.Path + "/" + Path.GetRelativePath(source, p)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!directories.SetEquals(snapshot.Directories.Where(d => Normalize(d).StartsWith(Normalize(root.Path) + "/", StringComparison.OrdinalIgnoreCase))))
                    throw new IOException("Game directories changed during backup. Scan the game again.");
            }
        }
        foreach (var file in snapshot.Files) await VerifyFileAsync(Child(directory, file.Path), file.Size, file.Sha256, token);
    }

    private static void ValidateUnexpectedProxyRoots(string directory, IEnumerable<string> roots, InstallationRecord current)
    {
        foreach (var root in roots.Where(IsProxyFile))
            if (Exists(Child(directory, root)) && !current.Files.Any(f => SameRelative(f.Path, root)))
                throw new IOException("An unrecognized proxy appeared after installation and will not be overwritten: " + root);
    }

    private static async Task ApplySnapshotAsync(string directory, Snapshot snapshot, string archiveRelative, CancellationToken token,
        IEnumerable<string>? additionalDisplacedRoots = null)
    {
        // Archive the displaced installation rather than deleting it, including new logs and extra user files.
        var archive = Child(directory, archiveRelative + "/displaced-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        foreach (var relative in snapshot.Roots.Select(r => r.Path).Union(additionalDisplacedRoots ?? [], StringComparer.OrdinalIgnoreCase))
        {
            ValidatePlannedRoot(relative);
            var current = Child(directory, relative);
            if (!Exists(current)) continue;
            EnsureNoReparseTree(current);
            var target = Child(archive, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(current)) Directory.Move(current, target); else File.Move(current, target);
        }
        var backup = Child(directory, snapshot.RelativeDirectory);
        foreach (var root in snapshot.Roots.Where(r => r.Existed && r.Directory)) Directory.CreateDirectory(Child(directory, root.Path));
        foreach (var subdirectory in snapshot.Directories) Directory.CreateDirectory(Child(directory, subdirectory));
        // Restore the previous proxy last, and the previous manager record after all its files.
        foreach (var file in snapshot.Files.OrderBy(f => IsProxyFile(f.Path) ? 1 : SameRelative(f.Path, RecordName) ? 2 : 0))
        {
            var target = Child(directory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyFileAsync(Child(backup, "original/" + file.Path), target, token);
            await VerifyFileAsync(target, file.Size, file.Sha256, token);
        }
    }

    private static async Task WriteJournalAsync(string directory, Snapshot snapshot, string? stage, CancellationToken token, string renderingExe) =>
        await WriteJsonAtomicAsync(Child(directory, JournalName), new Journal(1, directory, snapshot.RelativeDirectory, stage, renderingExe), token);

    private static async Task RecoverPendingAsync(string directory, CancellationToken token)
    {
        var path = Child(directory, JournalName);
        if (!File.Exists(path)) return;
        EnsureNoReparse(path);
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("The recovery journal is invalid.");
        Journal journal;
        try
        {
            journal = JsonSerializer.Deserialize<Journal>(await File.ReadAllTextAsync(path, token), Json)
                ?? throw new InvalidDataException("The recovery journal is empty.");
        }
        catch (JsonException error) { throw new InvalidDataException("The recovery journal contains invalid JSON.", error); }
        if (journal.SchemaVersion != 1 || string.IsNullOrWhiteSpace(journal.GameDirectory) || string.IsNullOrWhiteSpace(journal.SnapshotDirectory) ||
            !SamePath(journal.GameDirectory, directory) || !IsBackupRelative(journal.SnapshotDirectory))
            throw new InvalidDataException("The recovery journal is not valid for this game.");
        if (journal.RenderingExe is not null && !SamePath(Path.GetDirectoryName(Path.GetFullPath(journal.RenderingExe))!, directory))
            throw new InvalidDataException("The recovery executable is outside the game directory.");
        var gameExecutables = Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Take(257).ToArray();
        if (gameExecutables.Length > 256 || gameExecutables.Length == 0 && journal.RenderingExe is null)
            throw new IOException("The game process cannot be identified safely. Recovery was not started.");
        var processNames = gameExecutables.Select(Path.GetFileNameWithoutExtension)
            .Append(journal.RenderingExe is null ? null : Path.GetFileNameWithoutExtension(journal.RenderingExe))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        EnsureProcessesClosed(directory, processNames);
        var snapshot = await LoadSnapshotAsync(directory, journal.SnapshotDirectory, token);
        EnsureProcessesClosed(directory, processNames);
        await ApplySnapshotAsync(directory, snapshot, snapshot.RelativeDirectory, CancellationToken.None);
        File.Delete(path);
        if (journal.StageDirectory is not null) CleanupStage(directory, journal.StageDirectory);
    }

    private static void EnsureProcessesClosed(string directory, IReadOnlySet<string> processNames)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try { name = process.ProcessName; }
                catch (InvalidOperationException) { continue; }
                catch (System.ComponentModel.Win32Exception error)
                {
                    // Do not assume that a protected process is unrelated when even its name is unavailable.
                    try { if (process.HasExited) continue; }
                    catch (InvalidOperationException) { continue; }
                    catch (System.ComponentModel.Win32Exception) { }
                    throw new IOException("A running process could not be identified. Close the game before continuing.", error);
                }
                if (processNames.Contains(name))
                    throw new IOException("Close the game before installing, disabling, restoring or recovering the mod.");
                try
                {
                    if (process.MainModule?.FileName is { } exe && IsWithin(exe, directory))
                        throw new IOException("A process is using this game directory. Close it before continuing.");
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Its process name was read successfully and does not match any candidate rendering EXE.
                }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task RollBackOrThrowAsync(string directory, Snapshot snapshot, Exception operationError)
    {
        try
        {
            await ApplySnapshotAsync(directory, snapshot, snapshot.RelativeDirectory, CancellationToken.None);
            File.Delete(Child(directory, JournalName));
        }
        catch (Exception rollbackError)
        {
            throw new AggregateException("The operation failed and recovery could not finish. The backup and recovery journal were retained; close the game and retry.", operationError, rollbackError);
        }
    }

    private static FileStream AcquireLock(string directory)
    {
        var path = Child(directory, ".dlssg-manager.lock");
        EnsureNoReparse(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException error) { throw new IOException("Another operation is using this game, or its folder is not writable.", error); }
    }

    private static string ValidateGameDirectory(string path)
    {
        var directory = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(directory) || SamePath(directory, Path.GetPathRoot(directory)!))
            throw new InvalidOperationException("Select a game directory, not a drive root.");
        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) && (SamePath(directory, windows) || IsWithin(directory, windows)))
                throw new InvalidOperationException("Windows system folders cannot be used as game directories.");
        }
        EnsureNoReparse(directory);
        return directory;
    }

    private static bool IsX64Executable(string path)
    {
        try { using var reader = new PEReader(File.OpenRead(path)); return reader.PEHeaders.CoffHeader.Machine == Machine.Amd64 && reader.PEHeaders.PEHeader?.Magic == PEMagic.PE32Plus; }
        catch (BadImageFormatException) { return false; }
    }

    private static void EnsureGameClosed(string executable, string directory)
    {
        EnsureProcessesClosed(directory, new HashSet<string>([Path.GetFileNameWithoutExtension(executable)], StringComparer.OrdinalIgnoreCase));
    }

    private static void EnsureDiskSpace(string directory, long bytes)
    {
        var root = Path.GetPathRoot(directory);
        if (string.IsNullOrEmpty(root)) throw new IOException("The game drive could not be identified.");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < bytes) throw new IOException("There is not enough free space for the staged package and verified backup.");
    }

    private static long EstimateRootsBytes(string directory, IEnumerable<string> roots)
    {
        long total = 0;
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Child(directory, root);
            if (!Exists(path)) continue;
            if (Directory.Exists(path)) foreach (var file in EnumerateTreeFiles(path)) total = checked(total + new FileInfo(file).Length);
            else { EnsureNoReparse(path); total = checked(total + new FileInfo(path).Length); }
        }
        return total;
    }

    private static IEnumerable<string> EnumerateTreeFiles(string directory)
    {
        var count = 0;
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop(); EnsureNoReparse(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++count > 20000) throw new IOException("The mod directory contains too many files to back up safely.");
                EnsureNoReparse(entry);
                if (Directory.Exists(entry)) pending.Push(entry); else yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateTreeDirectories(string directory)
    {
        var count = 0;
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop(); EnsureNoReparse(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++count > 20000) throw new IOException("The mod directory contains too many entries to back up safely.");
                EnsureNoReparse(entry);
                if (!Directory.Exists(entry)) continue;
                yield return entry;
                pending.Push(entry);
            }
        }
    }

    private static void EnsureNoReparseTree(string path)
    {
        EnsureNoReparse(path);
        if (Directory.Exists(path)) foreach (var _ in EnumerateTreeFiles(path)) { }
    }

    private static void EnsureNoReparse(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are not supported for installation or backup paths.");
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current) break;
            current = parent;
        }
    }

    private static string Child(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\0'))
            throw new InvalidDataException("An invalid relative path was supplied.");
        var pieces = Normalize(relative).Split('/');
        if (pieces.Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("A relative path contains an unsafe component.");
        var full = Path.GetFullPath(Path.Combine(directory, Path.Combine(pieces)));
        if (!IsWithin(full, directory)) throw new InvalidDataException("A path escapes its allowed directory.");
        EnsureNoReparse(full);
        return full;
    }

    private static bool IsWithin(string path, string directory) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static bool SameRelative(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static bool IsDisabledProxy(string path) => ProxyNames.Any(p => SameRelative(path, p + ".disabled"));
    private static bool IsProxyFile(string path) => ProxyNames.Contains(path, StringComparer.OrdinalIgnoreCase) || IsDisabledProxy(path);
    private static bool IsSha256(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private static bool IsBackupRelative(string relative)
    {
        var pieces = Normalize(relative).Split('/');
        return pieces.Length == 2 && pieces[0] == BackupRoot && Guid.TryParseExact(pieces[1], "N", out _);
    }
    private static void ValidatePlannedRoot(string root)
    {
        if (root is not ("dlssg_sm86" or "dlssg_sm86.ini" or RecordName or "OptiScaler.ini") && !SameRelative(root, AnrProviderRoot) && !IsProxyFile(root))
            throw new InvalidDataException("An operation tried to modify an unexpected game file.");
    }
    private static void ValidateInstalledRelative(string relative, string proxy)
    {
        var normalized = Normalize(relative);
        if (!SameRelative(normalized, proxy) && !SameRelative(normalized, proxy + ".disabled") &&
            !SameRelative(normalized, "dlssg_sm86.ini") && !normalized.StartsWith("dlssg_sm86/", StringComparison.OrdinalIgnoreCase) &&
            !SameRelative(normalized, "OptiScaler.ini") && !normalized.StartsWith(AnrProviderRoot + "/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installed file inventory refers to an unexpected file.");
    }
    private static void DeletePlannedRoot(string directory, string relative)
    {
        ValidatePlannedRoot(relative);
        var target = Child(directory, relative);
        if (!Exists(target)) return;
        EnsureNoReparseTree(target);
        if (Directory.Exists(target)) Directory.Delete(target, true); else File.Delete(target);
    }
    private static void CleanupStage(string directory, string relative)
    {
        if (!relative.StartsWith(".dlssg-manager-stage-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(relative[".dlssg-manager-stage-".Length..], "N", out _)) return;
        try
        {
            var target = Child(directory, relative);
            if (!Directory.Exists(target)) return;
            EnsureNoReparseTree(target);
            Directory.Delete(target, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static async Task VerifyFileAsync(string path, long size, string sha256, CancellationToken token)
    {
        EnsureNoReparse(path);
        if (!IsSha256(sha256) || !File.Exists(path) || new FileInfo(path).Length != size)
            throw new IOException("A file is missing or has changed: " + Path.GetFileName(path));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!hash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A file has changed and will not be overwritten automatically: " + Path.GetFileName(path));
    }
    private static async Task<InstalledFile> InventoryFileAsync(string directory, string relative, CancellationToken token)
    {
        var path = Child(directory, relative); EnsureNoReparse(path);
        await using var stream = File.OpenRead(path);
        return new(Normalize(relative), stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, token)));
    }
    private static async Task CopyFileAsync(string source, string target, CancellationToken token)
    {
        EnsureNoReparse(source); EnsureNoReparse(Path.GetDirectoryName(target)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true);
    }
    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
        await JsonSerializer.SerializeAsync(stream, value, Json, token); await stream.FlushAsync(token); stream.Flush(true);
    }
    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken token)
    {
        EnsureNoReparse(path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { await WriteJsonAsync(temporary, value, token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static int CompareVersions(string first, string second)
    {
        if (first.StartsWith("FG-V.B", StringComparison.OrdinalIgnoreCase) && second.StartsWith("FG-V.B", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(first[6..], out var firstBeta) && int.TryParse(second[6..], out var secondBeta)) return firstBeta.CompareTo(secondBeta);
        static Version? Parse(string value) => Version.TryParse(value.TrimStart('v', 'V').Split('-', '+')[0], out var parsed) ? parsed : null;
        return Parse(first) is { } a && Parse(second) is { } b ? a.CompareTo(b) : 0;
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using DlssgManager.Core;

namespace DlssgManager.Tests;

public static class InstallationTests
{
    private sealed class ImmediateProgress(Action<OperationProgress> action) : IProgress<OperationProgress>
    { public void Report(OperationProgress value) => action(value); }

    public static async Task RunAsync()
    {
        var service = new InstallService();
        var options = new ModOptions();
        InstallService.ValidateOptions(options);
        await ThrowsAsync<ArgumentException>(() => Task.Run(() => InstallService.ValidateOptions(new() { Engine = "XeSS", Multiplier = 6 })));
        var configured = InstallService.ConfigureIni("[Runtime]\nMode=External\n[SuperResolution]\nMode=Off\n[FrameGeneration]\nMaxGeneratedFrames=3\n", options);
        Assert(configured.Contains("[Runtime]\nMode=Bundled") && configured.Contains("[SuperResolution]\nMode=Off"), "INI section-scoped editing");
        Assert(configured.Contains("MaxGeneratedFrames=1"), "2x is one generated frame");

        var fresh = await FixtureAsync();
        var installed = await service.InstallAsync(fresh.Scan(), fresh.Package, options);
        Assert(installed.Changed && installed.Record?.Version == "0.4.1", "fresh install");
        Assert(File.Exists(Path.Combine(fresh.Game, "version.dll")), "proxy installed");
        Assert((await File.ReadAllTextAsync(Path.Combine(fresh.Game, "unrelated.dll"))) == "game original", "unrelated original preserved");
        Assert((await File.ReadAllTextAsync(Path.Combine(fresh.Game, "dlssg_sm86.ini"))).Contains("MaxGeneratedFrames=1"), "INI configured");
        Assert(!(await service.InstallAsync(fresh.Scan(ModState.Managed), fresh.Package, options)).Changed, "same release and options is no-op");
        var settings = await service.InstallAsync(fresh.Scan(ModState.Managed), fresh.Package, options with { Multiplier = 3 });
        Assert(settings.Message == "settings_updated", "same package can change options");
        await service.DisableAsync(fresh.Game);
        Assert(!File.Exists(Path.Combine(fresh.Game, "version.dll")) && File.Exists(Path.Combine(fresh.Game, "version.dll.disabled")), "disable renames loader");
        await service.RestoreAsync(fresh.Game);
        var previous = await service.ReadRecordAsync(fresh.Game);
        Assert(previous is { State: "Installed", Options.Multiplier: 2 }, "restore after disable returns previous installation");
        Assert(!File.Exists(Path.Combine(fresh.Game, "version.dll.disabled")), "restore removes the owned disabled loader");

        var firstRestore = await FixtureAsync();
        var first = await service.InstallAsync(firstRestore.Scan(), firstRestore.Package, options);
        var extra = Path.Combine(firstRestore.Game, "dlssg_sm86", "my-notes.txt");
        await File.WriteAllTextAsync(extra, "preserve this extra file");
        await service.DisableAsync(firstRestore.Game);
        await service.RestoreAsync(firstRestore.Game);
        Assert(!File.Exists(Path.Combine(firstRestore.Game, "version.dll")) && !Directory.Exists(Path.Combine(firstRestore.Game, "dlssg_sm86")), "first install restores absence");
        Assert(!File.Exists(Path.Combine(firstRestore.Game, "version.dll.disabled")), "first restore also removes the owned disabled loader");
        Assert(Directory.EnumerateFiles(Path.Combine(firstRestore.Game, ".dlssg-manager-backups"), "my-notes.txt", SearchOption.AllDirectories).Any(), "extra files archived during restore");

        var update = await FixtureAsync();
        await service.InstallAsync(update.Scan(), update.Package, options);
        var oldIni = await File.ReadAllTextAsync(Path.Combine(update.Game, "dlssg_sm86.ini")) + "\n; player custom note\n";
        await File.WriteAllTextAsync(Path.Combine(update.Game, "dlssg_sm86.ini"), oldIni);
        await File.WriteAllTextAsync(Path.Combine(update.Game, "dlssg_sm86", "custom.txt"), "old extra");
        Directory.CreateDirectory(Path.Combine(update.Game, "dlssg_sm86", "empty-user-directory"));
        var newPackage = await CreatePackageAsync(Path.Combine(update.Root, "package-v2"), "0.4.2", 2);
        await service.InstallAsync(update.Scan(ModState.Managed), newPackage, options);
        Assert((await service.ReadRecordAsync(update.Game))?.Version == "0.4.2", "new release installed");
        await service.RestoreAsync(update.Game);
        Assert((await service.ReadRecordAsync(update.Game))?.Version == "0.4.1", "previous release restored");
        Assert(await File.ReadAllTextAsync(Path.Combine(update.Game, "dlssg_sm86.ini")) == oldIni, "edited configuration restored exactly");
        Assert(await File.ReadAllTextAsync(Path.Combine(update.Game, "dlssg_sm86", "custom.txt")) == "old extra", "extra file restored");
        Assert(Directory.Exists(Path.Combine(update.Game, "dlssg_sm86", "empty-user-directory")), "empty directory restored");
        await ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(update.Scan(ModState.Managed), update.Package with
        { Release = update.Package.Release with { Version = "0.3.0", ReleaseId = 3 } }, options));

        var collision = await FixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(collision.Game, "version.dll"), "another mod");
        await ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(collision.Scan(), collision.Package, options));
        Assert(await File.ReadAllTextAsync(Path.Combine(collision.Game, "version.dll")) == "another mod", "unknown proxy not overwritten");

        var tampered = await FixtureAsync();
        await service.InstallAsync(tampered.Scan(), tampered.Package, options);
        await File.WriteAllTextAsync(Path.Combine(tampered.Game, "version.dll"), "changed outside manager");
        await ThrowsAsync<IOException>(() => service.InstallAsync(tampered.Scan(ModState.Managed), tampered.Package, options with { Multiplier = 3 }));
        await ThrowsAsync<IOException>(() => service.RestoreAsync(tampered.Game));
        Assert(await File.ReadAllTextAsync(Path.Combine(tampered.Game, "version.dll")) == "changed outside manager", "modified managed binary not overwritten");

        var canceled = await FixtureAsync();
        using (var cancellation = new CancellationTokenSource())
        {
            var progress = new ImmediateProgress(p => { if (p.Stage == "install_backup") cancellation.Cancel(); });
            await ThrowsAsync<OperationCanceledException>(() => service.InstallAsync(canceled.Scan(), canceled.Package, options, progress, cancellation.Token));
        }
        Assert(!File.Exists(Path.Combine(canceled.Game, "version.dll")) && !File.Exists(Path.Combine(canceled.Game, InstallService.RecordName)), "cancellation leaves game unchanged");

        if (OperatingSystem.IsWindows())
        {
            var interrupted = await FixtureAsync();
            FileStream? heldLoader = null;
            try
            {
                var progress = new ImmediateProgress(p =>
                {
                    if (p.Stage != "install_commit") return;
                    var stage = Directory.GetDirectories(interrupted.Game, ".dlssg-manager-stage-*").Single();
                    heldLoader = new FileStream(Path.Combine(stage, "version.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
                });
                await ThrowsAsync<IOException>(() => service.InstallAsync(interrupted.Scan(), interrupted.Package, options, progress));
            }
            finally { heldLoader?.Dispose(); }
            Assert(!File.Exists(Path.Combine(interrupted.Game, "version.dll")) && !Directory.Exists(Path.Combine(interrupted.Game, "dlssg_sm86")), "mid-commit failure rolls runtime and INI back");
            Assert(!File.Exists(Path.Combine(interrupted.Game, ".dlssg-manager.transaction.json")), "successful rollback clears journal");
        }

        var recovery = await FixtureAsync();
        var recoveryInstall = await service.InstallAsync(recovery.Scan(), recovery.Package, options);
        await File.WriteAllTextAsync(Path.Combine(recovery.Game, ".dlssg-manager.transaction.json"), JsonSerializer.Serialize(new
        { SchemaVersion = 1, GameDirectory = recovery.Game, SnapshotDirectory = recoveryInstall.Record!.BackupDirectory, StageDirectory = (string?)null }));
        var runningName = Process.GetCurrentProcess().ProcessName;
        var runningCandidate = Path.Combine(recovery.Game, runningName + ".exe");
        File.Copy(Environment.ProcessPath!, runningCandidate);
        await ThrowsAsync<IOException>(() => service.RestoreAsync(recovery.Game));
        Assert(File.Exists(Path.Combine(recovery.Game, "version.dll")) && File.Exists(Path.Combine(recovery.Game, ".dlssg-manager.transaction.json")), "recovery blocks a matching process name before changing files");
        File.Delete(runningCandidate);
        await ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(recovery.Game));
        Assert(!File.Exists(Path.Combine(recovery.Game, "version.dll")) && !File.Exists(Path.Combine(recovery.Game, InstallService.RecordName)), "interrupted transaction recovers before the next operation");

        var brokenBackup = await FixtureAsync();
        await service.InstallAsync(brokenBackup.Scan(), brokenBackup.Package, options);
        var changedOptions = await service.InstallAsync(brokenBackup.Scan(ModState.Managed), brokenBackup.Package, options with { Multiplier = 3 });
        var backupProxy = Path.Combine(brokenBackup.Game, changedOptions.Record!.BackupDirectory.Replace('/', Path.DirectorySeparatorChar), "original", "version.dll");
        await File.WriteAllTextAsync(backupProxy, "corrupted backup");
        await ThrowsAsync<IOException>(() => service.RestoreAsync(brokenBackup.Game));
        Assert((await service.ReadRecordAsync(brokenBackup.Game))?.Options.Multiplier == 3, "corrupt backup refused before changing installed files");

        var manual = await FixtureAsync();
        foreach (var file in manual.Package.Files.Where(f => f.Path.StartsWith("dlssg_sm86/", StringComparison.OrdinalIgnoreCase) || f.Path == "dlssg_sm86.ini"))
        {
            var destination = Path.Combine(manual.Game, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(manual.Package.Directory, file.Path.Replace('/', Path.DirectorySeparatorChar)), destination);
        }
        File.Copy(Path.Combine(manual.Package.Directory, "alternatives", "winmm.dll"), Path.Combine(manual.Game, "winmm.dll"));
        var loadersInactiveDuringCommit = false;
        var adoptionProgress = new ImmediateProgress(p =>
        {
            if (p.Stage == "install_commit") loadersInactiveDuringCommit =
                !File.Exists(Path.Combine(manual.Game, "winmm.dll")) && !File.Exists(Path.Combine(manual.Game, "version.dll"));
        });
        await service.InstallAsync(manual.Scan(ModState.RecognizedManual), manual.Package, options, adoptionProgress);
        Assert(loadersInactiveDuringCommit, "old loaders deactivated before runtime replacement");
        Assert(File.Exists(Path.Combine(manual.Game, "version.dll")) && !File.Exists(Path.Combine(manual.Game, "winmm.dll")), "manual alternative proxy replaced without two active loaders");
        await File.WriteAllTextAsync(Path.Combine(manual.Game, "winmm.dll"), "a foreign proxy added later");
        await ThrowsAsync<IOException>(() => service.RestoreAsync(manual.Game));
        Assert(await File.ReadAllTextAsync(Path.Combine(manual.Game, "winmm.dll")) == "a foreign proxy added later", "restore refuses a foreign loader introduced later");
        File.Delete(Path.Combine(manual.Game, "winmm.dll"));
        await service.RestoreAsync(manual.Game);
        Assert(File.Exists(Path.Combine(manual.Game, "winmm.dll")) && !File.Exists(Path.Combine(manual.Game, "version.dll")), "manual alternative proxy restored");

        var traversal = await FixtureAsync();
        var malicious = traversal.Package with { Files = traversal.Package.Files.Append(new("dlssg_sm86/../../outside.dll", 0, new string('0', 64))).ToArray() };
        await ThrowsAsync<InvalidDataException>(() => service.InstallAsync(traversal.Scan(), malicious, options));
        Assert(!File.Exists(Path.Combine(traversal.Game, "version.dll")), "traversal package refused before mutation");

        var invalidRecord = await FixtureAsync();
        await service.InstallAsync(invalidRecord.Scan(), invalidRecord.Package, options);
        var recordPath = Path.Combine(invalidRecord.Game, InstallService.RecordName);
        var validJson = await File.ReadAllTextAsync(recordPath);
        var loaderBefore = await File.ReadAllBytesAsync(Path.Combine(invalidRecord.Game, "version.dll"));
        var malformedRecords = new List<string>();
        var nullOptions = JsonNode.Parse(validJson)!;
        nullOptions["Options"] = null;
        malformedRecords.Add(nullOptions.ToJsonString());
        var invalidOptions = JsonNode.Parse(validJson)!;
        invalidOptions["Options"]!["Multiplier"] = 99;
        malformedRecords.Add(invalidOptions.ToJsonString());
        var nullItem = JsonNode.Parse(validJson)!;
        nullItem["Files"]!.AsArray()[0] = null;
        malformedRecords.Add(nullItem.ToJsonString());
        var nullHash = JsonNode.Parse(validJson)!;
        nullHash["Files"]!.AsArray()[0]!["Sha256"] = null;
        malformedRecords.Add(nullHash.ToJsonString());
        malformedRecords.Add("{not-valid-json");
        foreach (var malformed in malformedRecords)
        {
            await File.WriteAllTextAsync(recordPath, malformed);
            await ThrowsAsync<InvalidDataException>(() => service.ReadRecordAsync(invalidRecord.Game));
            await ThrowsAsync<InvalidDataException>(() => service.InstallAsync(invalidRecord.Scan(ModState.Managed), invalidRecord.Package, options));
            var loaderAfter = await File.ReadAllBytesAsync(Path.Combine(invalidRecord.Game, "version.dll"));
            Assert(loaderBefore.SequenceEqual(loaderAfter), "invalid record does not mutate installed state");
            Assert(await File.ReadAllTextAsync(recordPath) == malformed, "invalid record is preserved for diagnosis");
        }
        await File.WriteAllTextAsync(recordPath, validJson);
        Assert((await service.ReadRecordAsync(invalidRecord.Game))?.Options.Multiplier == 2, "valid record remains readable after malformed record tests");

        Console.WriteLine("PASS installation: fresh, no-op, options, disable, rollback chain, update, manual adoption, collisions, tampering, cancellation, interrupted commit, process-safe crash recovery, backup corruption, path traversal, invalid/null record data");
    }

    private sealed record Fixture(string Root, string Game, string Exe, ReleasePackage Package)
    {
        public GameScan Scan(ModState state = ModState.NotInstalled) => new(Game, Exe, [new(Exe, "FixtureGame.exe", 100)],
            null, "310.9.16.0", state, state == ModState.Managed ? "0.4.1" : null, [new("fixture", CheckLevel.Pass)]);
    }

    private static async Task<Fixture> FixtureAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", "install-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var exe = Path.Combine(game, "FixtureGame.exe");
        if (OperatingSystem.IsWindows()) File.Copy(Environment.ProcessPath!, exe);
        else throw new PlatformNotSupportedException("Installation tests target Windows x64, matching the desktop application.");
        await File.WriteAllTextAsync(Path.Combine(game, "unrelated.dll"), "game original");
        return new(root, game, exe, await CreatePackageAsync(Path.Combine(root, "package"), "0.4.1", 1));
    }

    private static async Task<ReleasePackage> CreatePackageAsync(string directory, string version, int id)
    {
        Directory.CreateDirectory(directory);
        var files = new List<PackageFile>();
        foreach (var (relative, content) in new (string, string)[]
        {
            ("version.dll", "proxy " + version), ("alternatives/winmm.dll", "winmm " + version),
            ("dlssg_sm86.ini", "[General]\nEnabled=1\n[FrameGeneration]\nEngine=DLSS\nMaxGeneratedFrames=3\n[DLSSG]\nOptimized=1\nPreset=A\n[Logging]\nLevel=1\n[Runtime]\nMode=Bundled\n[SuperResolution]\nMode=Off\n"),
            ("dlssg_sm86/backend/sm86_backend.dll", "backend " + version),
            ("dlssg_sm86/kernels/fg_kernels.dll", "kernels " + version),
            ("dlssg_sm86/runtime/nvngx_dlssg.dll", "runtime " + version)
        })
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
            var bytes = await File.ReadAllBytesAsync(path);
            files.Add(new(relative, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return new(new(version, id, id, "fixture.zip", "https://github.com/sdli1995/dlssg_for_sm86/releases/download/" + version + "/fixture.zip",
            100, new string('a', 64), DateTimeOffset.UtcNow, "https://github.com/sdli1995/dlssg_for_sm86/releases/tag/" + version),
            directory, files, true, [new("package_hash", CheckLevel.Pass)]);
    }

    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL installation: " + name); }
    private static async Task ThrowsAsync<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}

using System.Security.Cryptography;
using DlssgManager.Core;

namespace DlssgManager.Tests;

public static class AnrInstallationTests
{
    private sealed class ImmediateProgress(Action<OperationProgress> action) : IProgress<OperationProgress>
    { public void Report(OperationProgress progress) => action(progress); }
    private sealed record Fixture(string Root, string Game, string Exe, ReleasePackage Full, ReleasePackage Addon, ReleasePackage Sm86)
    {
        public GameScan Scan(ModProvider provider, ModState state = ModState.Managed) => new(Game, Exe,
            [new(Exe, "SyntheticGame.exe", 1)], null, "310.9.16.0", state, null, [new("fixture", CheckLevel.Pass)]) { Provider = provider };
    }
    private static readonly ModOptions Anr = new() { Provider = ModProvider.Anr, Engine = "ANR", Proxy = "dxgi.dll" };

    public static async Task RunAsync()
    {
        var installer = new InstallService();
        var f = await CreateFixtureAsync();
        var first = await installer.InstallAsync(f.Scan(ModProvider.Anr, ModState.NotInstalled), f.Full, Anr);
        Assert(first.Record is { Provider: ModProvider.Anr, Variant: "full", ExternalOptiScaler: false }, "full record identifies ANR");
        Assert(File.Exists(Path.Combine(f.Game, "dxgi.dll")) && !File.Exists(Path.Combine(f.Game, "install.ps1")), "only reviewed payload installed");
        Assert((await File.ReadAllTextAsync(Path.Combine(f.Game, InstallService.AnrIniPath))).Contains("max_generated = 1"), "ANR starts at 2x");
        Assert(!(await installer.InstallAsync(f.Scan(ModProvider.Anr), f.Full, Anr)).Changed, "ANR identical options no-op");
        await installer.InstallAsync(f.Scan(ModProvider.Anr), f.Full, Anr with { Multiplier = 4, AnrSceneCut = false });
        await installer.DisableAsync(f.Game);
        Assert(!File.Exists(Path.Combine(f.Game, "dxgi.dll")) && File.Exists(Path.Combine(f.Game, "dxgi.dll.disabled")), "full disable deactivates loader");
        await installer.RestoreAsync(f.Game);
        Assert((await installer.ReadRecordAsync(f.Game))?.Options.Multiplier == 2, "ANR restore after disable retains prior version/options");
        await installer.RestoreAsync(f.Game);
        Assert(!File.Exists(Path.Combine(f.Game, "dxgi.dll")) && !Directory.Exists(Path.Combine(f.Game, InstallService.AnrProviderRoot)), "first ANR restore removes owned payload");

        var switchFixture = await CreateFixtureAsync();
        await installer.InstallAsync(switchFixture.Scan(ModProvider.Sm86, ModState.NotInstalled), switchFixture.Sm86, new());
        await installer.InstallAsync(switchFixture.Scan(ModProvider.Anr), switchFixture.Full, Anr);
        Assert(!File.Exists(Path.Combine(switchFixture.Game, "version.dll")) && !Directory.Exists(Path.Combine(switchFixture.Game, "dlssg_sm86")), "switch deactivates and removes SM86 payload");
        await installer.InstallAsync(switchFixture.Scan(ModProvider.Sm86), switchFixture.Sm86, new());
        Assert(File.Exists(Path.Combine(switchFixture.Game, "version.dll")) && !File.Exists(Path.Combine(switchFixture.Game, "dxgi.dll")), "reverse switch has one loader");
        await installer.InstallAsync(switchFixture.Scan(ModProvider.Anr), switchFixture.Full, Anr);
        await installer.RestoreAsync(switchFixture.Game);
        Assert((await installer.ReadRecordAsync(switchFixture.Game))?.Provider == ModProvider.Sm86, "restore crosses provider boundary");
        await installer.RestoreAsync(switchFixture.Game);
        Assert((await installer.ReadRecordAsync(switchFixture.Game))?.Provider == ModProvider.Anr, "restore chain reaches older ANR installation");
        await installer.DisableAsync(switchFixture.Game);
        await installer.InstallAsync(switchFixture.Scan(ModProvider.Sm86), switchFixture.Sm86, new());
        Assert(!File.Exists(Path.Combine(switchFixture.Game, "dxgi.dll.disabled")) && File.Exists(Path.Combine(switchFixture.Game, "version.dll")), "switch from disabled ANR clears its owned disabled loader");
        await installer.RestoreAsync(switchFixture.Game);
        Assert((await installer.ReadRecordAsync(switchFixture.Game)) is { Provider: ModProvider.Anr, State: "Disabled" }, "restore returns disabled previous provider");

        var ext = await CreateFixtureAsync();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures/SyntheticOptiScaler.dll"), Path.Combine(ext.Game, "dxgi.dll"));
        const string originalConfig = "[Libraries]\r\nOptiDllPath=auto\r\n[FrameGen]\r\nFGInput=upscaler\r\nFGNvngxReplacement=OtherProvider\r\n[User]\r\nKeepThis=42\r\n";
        await File.WriteAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini"), originalConfig);
        var unrelatedPlugin = Path.Combine(ext.Game, "OptiScaler/custom/Unrelated/notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedPlugin)!);
        await File.WriteAllTextAsync(unrelatedPlugin, "keep external plugin");
        Assert(await InstallService.IsCompatibleOptiScalerAsync(ext.Game, "dxgi.dll"), "compatible external OptiScaler recognized");
        await ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(ext.Scan(ModProvider.Anr, ModState.NotInstalled), ext.Full, Anr));
        var addon = await installer.InstallAsync(ext.Scan(ModProvider.Anr, ModState.NotInstalled), ext.Addon, Anr);
        Assert(addon.Record is { ExternalOptiScaler: true, OriginalFgInput: "upscaler", OriginalFgReplacement: "OtherProvider" }, "add-on records prior provider settings");
        Assert((await File.ReadAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini"))).Contains("KeepThis=42"), "add-on preserves unrelated config");
        var optiChanged = (await File.ReadAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini"))).Replace("FGNvngxReplacement=Apex Neural Rendering FG", "FGNvngxReplacement=OtherProvider");
        await File.WriteAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini"), optiChanged);
        Assert((await installer.InstallAsync(ext.Scan(ModProvider.Anr), ext.Addon, Anr)).Changed, "explicit apply repairs changed ANR selection rather than a false no-op");
        await installer.DisableAsync(ext.Game);
        Assert(File.Exists(Path.Combine(ext.Game, "dxgi.dll")) && !File.Exists(Path.Combine(ext.Game, "dxgi.dll.disabled")), "add-on disable preserves external loader");
        Assert((await File.ReadAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini"))).Contains("FGNvngxReplacement=OtherProvider"), "add-on disable restores original provider");
        await installer.InstallAsync(ext.Scan(ModProvider.Anr), ext.Addon, Anr);
        await installer.InstallAsync(ext.Scan(ModProvider.Sm86), ext.Sm86, new());
        Assert(!File.Exists(Path.Combine(ext.Game, "dxgi.dll")) && File.Exists(Path.Combine(ext.Game, "version.dll")), "external add-on switch leaves one active loader");
        Assert((await File.ReadAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini"))).Contains("FGNvngxReplacement=OtherProvider"), "SM86 switch restores dormant external OptiScaler configuration");
        await installer.InstallAsync(ext.Scan(ModProvider.Sm86), ext.Sm86, new ModOptions { Multiplier = 3 });
        await installer.InstallAsync(ext.Scan(ModProvider.Anr), ext.Addon, Anr);
        Assert(File.Exists(Path.Combine(ext.Game, "dxgi.dll")) && !File.Exists(Path.Combine(ext.Game, "version.dll")), "switch-back restores verified external loader from backup");
        Assert(await File.ReadAllTextAsync(unrelatedPlugin) == "keep external plugin", "provider switching preserves unrelated external plugin");
        await installer.RestoreAsync(ext.Game);
        await installer.RestoreAsync(ext.Game);
        await installer.RestoreAsync(ext.Game);
        await installer.RestoreAsync(ext.Game);
        await installer.RestoreAsync(ext.Game);
        await installer.RestoreAsync(ext.Game);
        Assert(await installer.ReadRecordAsync(ext.Game) is null && File.Exists(Path.Combine(ext.Game, "dxgi.dll")), "add-on restore chain returns external OptiScaler");
        Assert(await File.ReadAllTextAsync(Path.Combine(ext.Game, "OptiScaler.ini")) == originalConfig, "original external config restored byte-for-byte");
        Assert(await File.ReadAllTextAsync(unrelatedPlugin) == "keep external plugin", "external plugin survived complete restore chain");

        var canceled = await CreateFixtureAsync();
        using (var cts = new CancellationTokenSource())
            await ThrowsAsync<OperationCanceledException>(() => installer.InstallAsync(canceled.Scan(ModProvider.Anr, ModState.NotInstalled), canceled.Full, Anr,
                new ImmediateProgress(p => { if (p.Stage == "install_backup") cts.Cancel(); }), cts.Token));
        Assert(!File.Exists(Path.Combine(canceled.Game, "dxgi.dll")), "ANR cancellation leaves game unchanged");

        var failing = await CreateFixtureAsync();
        FileStream? held = null;
        try
        {
            await ThrowsAsync<IOException>(() => installer.InstallAsync(failing.Scan(ModProvider.Anr, ModState.NotInstalled), failing.Full, Anr,
                new ImmediateProgress(p =>
                {
                    if (p.Stage == "install_commit") held = new FileStream(Path.Combine(Directory.GetDirectories(failing.Game, ".dlssg-manager-stage-*").Single(), "dxgi.dll"), FileMode.Open, FileAccess.Read, FileShare.None);
                })));
        }
        finally { held?.Dispose(); }
        Assert(!Directory.Exists(Path.Combine(failing.Game, InstallService.AnrProviderRoot)) && !File.Exists(Path.Combine(failing.Game, "OptiScaler.ini")), "ANR interrupted commit rolls back provider and config");

        var tampered = await CreateFixtureAsync();
        await installer.InstallAsync(tampered.Scan(ModProvider.Anr, ModState.NotInstalled), tampered.Full, Anr);
        await File.WriteAllTextAsync(Path.Combine(tampered.Game, InstallService.AnrProviderRoot, "dlssg/kernels/pack.nrx"), "external edit");
        await ThrowsAsync<IOException>(() => installer.RestoreAsync(tampered.Game));
        await ThrowsAsync<IOException>(() => installer.InstallAsync(tampered.Scan(ModProvider.Sm86), tampered.Sm86, new()));

        var collision = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(collision.Game, "version.dll"), "foreign loader");
        await ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(collision.Scan(ModProvider.Anr, ModState.NotInstalled), collision.Full, Anr));
        var bad = await CreateFixtureAsync();
        await ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(bad.Scan(ModProvider.Sm86, ModState.NotInstalled), bad.Full, Anr));
        await ThrowsAsync<ArgumentException>(() => Task.Run(() => InstallService.ValidateOptions(Anr with { AnrPath = "../../evil" })));
        Console.WriteLine("PASS ANR transactions: full/add-on, options, both provider switches, external OptiScaler preservation, restore chain, cancellation, interrupted commit, tampering and conflicts");
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", "anr-install-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var exe = Path.Combine(game, "SyntheticGame.exe"); File.Copy(Environment.ProcessPath!, exe);
        var full = await PackageAsync(Path.Combine(root, "full"), ModProvider.Anr, "full");
        var addon = await PackageAsync(Path.Combine(root, "addon"), ModProvider.Anr, "addon");
        var sm86 = await PackageAsync(Path.Combine(root, "sm86"), ModProvider.Sm86, "full");
        return new(root, game, exe, full, addon, sm86);
    }

    private static async Task<ReleasePackage> PackageAsync(string directory, ModProvider provider, string variant)
    {
        var files = new Dictionary<string, string>();
        string version = provider == ModProvider.Anr ? "FG-V.B15" : "0.4.1";
        if (provider == ModProvider.Anr)
        {
            var prefix = variant == "full" ? "game/" + InstallService.AnrProviderRoot : "Apex Neural Rendering FG";
            files[prefix + "/dlssg/nvngx_dlssg.dll"] = "synthetic ANR provider";
            files[prefix + "/dlssg/nr_fg.ini"] = "path = auto\nmodel_size = large\nnits_per_unit = 80\nmax_generated = 5\nscene_cut = 1\n";
            files[prefix + "/dlssg/kernels/pack.nrx"] = "synthetic kernel";
            files[prefix + "/dlssg/programs/example/pack.nrx"] = "synthetic model";
            files["install.ps1"] = "must never be installed or run";
            if (variant == "full")
            {
                files["game/dxgi.dll"] = "synthetic loader";
                files["game/OptiScaler.ini"] = "[Libraries]\nOptiDllPath=auto\n[FrameGen]\nFGInput=nvngxfg\nFGNvngxReplacement=Apex Neural Rendering FG\n";
            }
        }
        else
        {
            files["version.dll"] = "synthetic SM86 loader";
            files["dlssg_sm86.ini"] = "[FrameGeneration]\nEngine=DLSS\n[DLSSG]\nPreset=A\n";
            files["dlssg_sm86/backend/sm86_backend.dll"] = "synthetic backend";
            files["dlssg_sm86/kernels/fg_kernels.dll"] = "synthetic kernel";
            files["dlssg_sm86/runtime/nvngx_dlssg.dll"] = "synthetic runtime";
        }
        var inventory = new List<PackageFile>();
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
            var bytes = await File.ReadAllBytesAsync(path);
            inventory.Add(new(relative, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        return new(new(version, provider == ModProvider.Anr ? 150 : 41, variant == "addon" ? 151 : 150, "synthetic.zip",
            "https://github.com/apex-compute/ANR/releases/download/FG-V.B15/synthetic.zip", 100, new string('b', 64), DateTimeOffset.UtcNow,
            "https://github.com/apex-compute/ANR/releases/tag/FG-V.B15"), directory, inventory, true, [new("fixture", CheckLevel.Pass)]) { Provider = provider, Variant = variant };
    }
    private static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL ANR installation: " + name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}

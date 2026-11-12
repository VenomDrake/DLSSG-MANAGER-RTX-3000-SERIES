using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

public sealed partial class InstallService
{
    public const string AnrProviderRoot = "OptiScaler/custom/Apex Neural Rendering FG";
    public const string AnrIniPath = AnrProviderRoot + "/dlssg/nr_fg.ini";
    private const string ReviewedOptiScalerHash = "52d7b390b129deb026333835a85cece808cb99ba9201fe58dca9d2d35ac8fd6b";

    private static void ValidateAnrOptions(ModOptions options)
    {
        if (options.Engine != "ANR" || options.Multiplier is < 2 or > 6 ||
            options.AnrPath is not ("auto" or "coopmat" or "packed" or "dp4a" or "portable" or "portable-linalg" or "portable-xmx") ||
            options.AnrNitsPerUnit is < 40 or > 1000 || options.Proxy is not ("dxgi.dll" or "winmm.dll" or "version.dll"))
            throw new ArgumentException("The ANR options are invalid or unsupported.", nameof(options));
    }

    private static bool IsMutableConfiguration(string path) => SameRelative(path, "dlssg_sm86.ini") ||
        SameRelative(path, "OptiScaler.ini") || SameRelative(path, AnrIniPath);

    public static async Task<bool> IsCompatibleOptiScalerAsync(string directory, string proxy, CancellationToken cancellationToken = default)
    {
        try
        {
            if (proxy is not ("dxgi.dll" or "winmm.dll" or "version.dll")) return false;
            var ini = Child(directory, "OptiScaler.ini");
            var loader = Child(directory, proxy);
            if (!File.Exists(ini) || !File.Exists(loader) || !IsX64Executable(loader) || new FileInfo(ini).Length > 1024 * 1024) return false;
            var text = await File.ReadAllTextAsync(ini, cancellationToken);
            ValidateOptiScalerConfiguration(text);
            if (!Directory.Exists(Child(directory, "OptiScaler")) &&
                !string.Equals(GetIniValue(text, "Libraries", "OptiDllPath"), "OptiScaler", StringComparison.OrdinalIgnoreCase)) return false;
            var metadata = FileVersionInfo.GetVersionInfo(loader);
            if ((metadata.ProductName ?? "").Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) ||
                (metadata.FileDescription ?? "").Contains("OptiScaler", StringComparison.OrdinalIgnoreCase)) return true;
            await using var stream = File.OpenRead(loader);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).Equals(ReviewedOptiScalerHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or BadImageFormatException) { return false; }
    }

    private static async Task<bool> AnrConfigurationMatchesAsync(string directory, ModOptions options, CancellationToken token)
    {
        var opti = await ReadOptiScalerIniAsync(directory, token);
        var anrPath = Child(directory, AnrIniPath);
        if (!File.Exists(anrPath) || new FileInfo(anrPath).Length > 64 * 1024) return false;
        var anr = await File.ReadAllTextAsync(anrPath, token);
        return GetIniValue(opti, "FrameGen", "FGInput") == "nvngxfg" &&
            GetIniValue(opti, "FrameGen", "FGNvngxReplacement") == "Apex Neural Rendering FG" &&
            ConfigureAnrIni(anr, options) == anr;
    }

    private static void ValidateOptiScalerConfiguration(string text)
    {
        // The pre-release supports the standard directory only. An arbitrary OptiDllPath
        // must never allow an install/uninstall to reach outside its reviewed roots.
        var optiPath = GetIniValue(text, "Libraries", "OptiDllPath");
        if (optiPath is not null && !optiPath.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !optiPath.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase) && optiPath.Length != 0)
            throw new InvalidDataException("This pre-release supports only the default OptiScaler directory.");
        if (GetIniValue(text, "FrameGen", "FGNvngxReplacement") is null)
            throw new InvalidDataException("The installed OptiScaler configuration does not declare custom frame generation providers.");
    }

    private static string? GetIniValue(string source, string section, string key)
    {
        bool inside = false;
        string? value = null;
        int found = 0;
        foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('[')) { inside = trimmed.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); continue; }
            int equals = trimmed.IndexOf('=');
            if (!inside || equals <= 0 || !trimmed[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            value = trimmed[(equals + 1)..].Trim();
            if (++found > 1) throw new InvalidDataException("The OptiScaler configuration contains duplicate settings.");
        }
        return value;
    }

    private static string RestoreOptiScalerSettings(string text, InstallationRecord record) =>
        SetOrRemoveIniValue(SetOrRemoveIniValue(text, "FrameGen", "FGInput", record.OriginalFgInput),
            "FrameGen", "FGNvngxReplacement", record.OriginalFgReplacement);

    private static string SetOrRemoveIniValue(string text, string section, string key, string? value)
    {
        if (value is not null) return SetIniValue(text, section, key, value);
        bool inside = false;
        return string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Where(line =>
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[')) { inside = trimmed.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); return true; }
            int equals = trimmed.IndexOf('=');
            return !inside || equals <= 0 || !trimmed[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
        }));
    }

    public static string ConfigureAnrIni(string text, ModOptions options)
    {
        ValidateAnrOptions(options);
        foreach (var (key, value) in new[]
        {
            ("path", options.AnrPath), ("model_size", "large"),
            ("nits_per_unit", options.AnrNitsPerUnit.ToString(CultureInfo.InvariantCulture)),
            ("max_generated", (options.Multiplier - 1).ToString(CultureInfo.InvariantCulture)),
            ("scene_cut", options.AnrSceneCut ? "1" : "0")
        })
        {
            var expression = new Regex(@"(?m)^\s*" + Regex.Escape(key) + @"\s*=[^\r\n]*", RegexOptions.CultureInvariant);
            if (expression.Matches(text).Count > 1) throw new InvalidDataException("ANR configuration contains duplicate settings.");
            text = expression.IsMatch(text) ? expression.Replace(text, key + " = " + value, 1) : text.TrimEnd() + "\n" + key + " = " + value + "\n";
        }
        return text;
    }

    private static void ValidateExternalOptiScalerRecord(InstallationRecord record)
    {
        if (!record.ExternalOptiScaler) return;
        if (record.ExternalProxy is not ("dxgi.dll" or "winmm.dll" or "version.dll") ||
            !IsSha256(record.ExternalProxySha256) || record.ExternalProxySize <= 0 ||
            record.OriginalFgInput?.IndexOfAny(['\r', '\n']) >= 0 || record.OriginalFgReplacement?.IndexOfAny(['\r', '\n']) >= 0 ||
            record.Provider == ModProvider.Anr && (record.Variant != "addon" || !SameRelative(record.Proxy, record.ExternalProxy)) ||
            record.Provider == ModProvider.Sm86 && (record.ExternalLoaderBackupDirectory is null || !IsBackupRelative(record.ExternalLoaderBackupDirectory)))
            throw new InvalidDataException("The external OptiScaler record is invalid.");
    }

    private static async Task<InstallationRecord> BindExternalOptiScalerAsync(string directory, InstallationRecord record, CancellationToken token)
    {
        if (!await IsCompatibleOptiScalerAsync(directory, record.Proxy, token))
            throw new InvalidDataException("A compatible OptiScaler loader could not be identified.");
        var text = await File.ReadAllTextAsync(Child(directory, "OptiScaler.ini"), token);
        if (string.Equals(GetIniValue(text, "FrameGen", "FGNvngxReplacement"), "Apex Neural Rendering FG", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The existing configuration already selects an unmanaged ANR provider. Restore it before adoption.");
        var proxy = await InventoryFileAsync(directory, record.Proxy, token);
        var stagedProxy = record.Files.Single(f => SameRelative(f.Path, record.Proxy));
        if (proxy.Size != stagedProxy.Size || !proxy.Sha256.Equals(stagedProxy.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The external OptiScaler loader changed during staging. Scan the game again.");
        return record with
        {
            ExternalProxy = record.Proxy, ExternalProxySize = proxy.Size, ExternalProxySha256 = proxy.Sha256,
            OriginalFgInput = GetIniValue(text, "FrameGen", "FGInput"),
            OriginalFgReplacement = GetIniValue(text, "FrameGen", "FGNvngxReplacement")
        };
    }

    private static async Task<List<InstalledFile>> StageSm86PackageAsync(ReleasePackage package, string stage, ModOptions options,
        string directory, InstallationRecord? previous, CancellationToken token)
    {
        var files = await StagePackageAsync(package, stage, options, token);
        if (previous?.ExternalOptiScaler == true)
        {
            var text = await ReadOptiScalerIniAsync(directory, token);
            await File.WriteAllTextAsync(Child(stage, "OptiScaler.ini"), RestoreOptiScalerSettings(text, previous), new UTF8Encoding(false), token);
            files.Add(await InventoryFileAsync(stage, "OptiScaler.ini", token));
        }
        return files;
    }

    private static async Task<string> ReadOptiScalerIniAsync(string directory, CancellationToken token)
    {
        var path = Child(directory, "OptiScaler.ini");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The OptiScaler configuration is missing or too large.");
        var text = await File.ReadAllTextAsync(path, token);
        ValidateOptiScalerConfiguration(text);
        return text;
    }

    private static async Task<List<InstalledFile>> StageAnrPackageAsync(ReleasePackage package, string stage, ModOptions options,
        string directory, InstallationRecord? previous, CancellationToken token)
    {
        if (package.Variant is not ("full" or "addon")) throw new InvalidDataException("Unknown ANR package variant.");
        if (previous is { Provider: ModProvider.Anr, ExternalOptiScaler: false } && package.Variant == "addon" ||
            previous?.ExternalOptiScaler == true && package.Variant != "addon")
            throw new InvalidOperationException("Keep the installed OptiScaler package variant while updating or switching providers.");
        var prefix = package.Variant == "full" ? "game/" + AnrProviderRoot + "/" : "Apex Neural Rendering FG/";
        var sources = package.Files.Where(f => Normalize(f.Path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sources.Length is < 4 or > 2048 || !sources.Any(f => SameRelative(f.Path, prefix + "dlssg/nvngx_dlssg.dll")) ||
            !sources.Any(f => SameRelative(f.Path, prefix + "dlssg/nr_fg.ini")) ||
            !sources.Any(f => SameRelative(f.Path, prefix + "dlssg/kernels/pack.nrx")))
            throw new InvalidDataException("The verified ANR provider inventory is incomplete.");
        var files = new List<InstalledFile>();
        foreach (var sourceFile in sources)
        {
            var source = Child(package.Directory, sourceFile.Path);
            await VerifyFileAsync(source, sourceFile.Size, sourceFile.Sha256, token);
            var relative = AnrProviderRoot + "/" + Normalize(sourceFile.Path)[prefix.Length..];
            ValidateInstalledRelative(relative, options.Proxy);
            var target = Child(stage, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (SameRelative(relative, AnrIniPath))
                await File.WriteAllTextAsync(target, ConfigureAnrIni(await File.ReadAllTextAsync(source, token), options), new UTF8Encoding(false), token);
            else { await CopyFileAsync(source, target, token); await VerifyFileAsync(target, sourceFile.Size, sourceFile.Sha256, token); }
            files.Add(await InventoryFileAsync(stage, relative, token));
        }
        string optiText;
        string loaderSource;
        if (package.Variant == "full")
        {
            var iniFile = package.Files.SingleOrDefault(f => SameRelative(f.Path, "game/OptiScaler.ini"))
                ?? throw new InvalidDataException("The full ANR package has no OptiScaler configuration.");
            var loader = package.Files.SingleOrDefault(f => SameRelative(f.Path, "game/dxgi.dll"))
                ?? throw new InvalidDataException("The full ANR package has no OptiScaler loader.");
            await VerifyFileAsync(Child(package.Directory, iniFile.Path), iniFile.Size, iniFile.Sha256, token);
            loaderSource = Child(package.Directory, loader.Path);
            await VerifyFileAsync(loaderSource, loader.Size, loader.Sha256, token);
            optiText = await File.ReadAllTextAsync(Child(package.Directory, iniFile.Path), token);
        }
        else
        {
            optiText = await ReadOptiScalerIniAsync(directory, token);
            if (previous?.ExternalOptiScaler == true && !SameRelative(options.Proxy, previous.ExternalProxy!))
                throw new InvalidOperationException("The add-on must retain the existing OptiScaler loader name.");
            if (previous is { Provider: ModProvider.Sm86, ExternalOptiScaler: true })
            {
                _ = await LoadSnapshotAsync(directory, previous.ExternalLoaderBackupDirectory!, token);
                loaderSource = Child(directory, previous.ExternalLoaderBackupDirectory + "/original/" + previous.ExternalProxy);
                await VerifyFileAsync(loaderSource, previous.ExternalProxySize, previous.ExternalProxySha256!, token);
            }
            else
            {
                loaderSource = Child(directory, options.Proxy);
                if (previous?.ExternalOptiScaler != true && !await IsCompatibleOptiScalerAsync(directory, options.Proxy, token))
                    throw new InvalidOperationException("Install a compatible OptiScaler first, or use the full package.");
            }
        }
        ValidateOptiScalerConfiguration(optiText);
        optiText = SetIniValue(SetIniValue(optiText, "FrameGen", "FGInput", "nvngxfg"), "FrameGen", "FGNvngxReplacement", "Apex Neural Rendering FG");
        await File.WriteAllTextAsync(Child(stage, "OptiScaler.ini"), optiText, new UTF8Encoding(false), token);
        await CopyFileAsync(loaderSource, Child(stage, options.Proxy), token);
        files.Add(await InventoryFileAsync(stage, "OptiScaler.ini", token));
        files.Add(await InventoryFileAsync(stage, options.Proxy, token));
        if (files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("The ANR inventory has duplicate paths.");
        return files;
    }

    private static bool HasOptiScalerFiles(string directory)
    {
        var root = Child(directory, "OptiScaler");
        return Directory.Exists(root) && EnumerateTreeFiles(root).Any();
    }

    private static async Task ValidateProviderCollisionsAsync(string directory, GameScan scan, ReleasePackage package,
        InstallationRecord? previous, CancellationToken token)
    {
        if (scan.ModState == ModState.UnknownConflict) throw new InvalidOperationException("An unknown mod or proxy is already installed.");
        if (previous?.Provider != ModProvider.Sm86 && (Exists(Child(directory, "dlssg_sm86")) || Exists(Child(directory, "dlssg_sm86.ini"))))
            throw new InvalidOperationException("Restore or adopt the existing SM86 installation before switching providers.");
        if (Exists(Child(directory, AnrProviderRoot)) && previous?.Provider != ModProvider.Anr)
            throw new InvalidOperationException("An unmanaged ANR provider exists and will not be overwritten.");
        if (package.Provider == ModProvider.Anr && package.Variant == "full" &&
            (Exists(Child(directory, "OptiScaler.ini")) || HasOptiScalerFiles(directory)) &&
            previous is not { Provider: ModProvider.Anr, ExternalOptiScaler: false })
            throw new InvalidOperationException("OptiScaler already exists. Use its compatible add-on package instead.");
        var allowed = previous is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) :
            new HashSet<string>([previous.State == "Disabled" && !(previous.Provider == ModProvider.Anr && previous.ExternalOptiScaler)
                ? previous.Proxy + ".disabled" : previous.Proxy], StringComparer.OrdinalIgnoreCase);
        foreach (var proxy in ProxyNames)
        {
            if (!Exists(Child(directory, proxy))) continue;
            if (allowed.Contains(proxy)) continue;
            if (previous is null && package.Provider == ModProvider.Anr && package.Variant == "addon" && await IsCompatibleOptiScalerAsync(directory, proxy, token))
                allowed.Add(proxy);
            else throw new InvalidOperationException("An unrecognized proxy exists: " + proxy);
        }
        if (allowed.Count > 1) throw new InvalidOperationException("More than one loader exists. Resolve the conflict before installing.");
    }

    private static async Task<InstallOutcome> DisableAnrAddonAsync(string directory, InstallationRecord record, CancellationToken token)
    {
        var text = await ReadOptiScalerIniAsync(directory, token);
        var snapshot = await CreateSnapshotAsync(directory, ["OptiScaler.ini", RecordName], token);
        await VerifySnapshotSourceAsync(directory, snapshot, token);
        await WriteJournalAsync(directory, snapshot, null, token, record.RenderingExe);
        try
        {
            token.ThrowIfCancellationRequested();
            EnsureGameClosed(record.RenderingExe, directory);
            var target = Child(directory, "OptiScaler.ini");
            var temporary = Child(directory, ".dlssg-manager-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                var staged = Child(temporary, "OptiScaler.ini");
                await File.WriteAllTextAsync(staged, RestoreOptiScalerSettings(text, record), new UTF8Encoding(false), token);
                File.Move(staged, target, true);
            }
            finally { CleanupStage(directory, Path.GetFileName(temporary)); }
            var inventory = await InventoryFileAsync(directory, "OptiScaler.ini", CancellationToken.None);
            var updated = record with { State = "Disabled", Files = record.Files.Select(f => SameRelative(f.Path, "OptiScaler.ini") ? inventory : f).ToArray() };
            await WriteJsonAtomicAsync(Child(directory, RecordName), updated, CancellationToken.None);
            File.Delete(Child(directory, JournalName));
            return new(updated, "disable_done", true);
        }
        catch (Exception error) { await RollBackOrThrowAsync(directory, snapshot, error); throw; }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DlssgManager.Core;

/// <summary>Read-only machine inspection. Registry HAGS is deliberately not reported as active.</summary>
public sealed class HardwareService
{
    public async Task<HardwareInfo> InspectAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<CheckResult>();
        bool supportedOs = OperatingSystem.IsWindowsVersionAtLeast(10) && Environment.Is64BitOperatingSystem && Environment.Is64BitProcess;
        checks.Add(new("windows_x64", supportedOs ? CheckLevel.Pass : CheckLevel.Blocked, Environment.OSVersion.VersionString));
        var gpus = new List<GpuInfo>();
        string smi = FindNvidiaSmi();
        if (!string.IsNullOrEmpty(smi))
        {
            var response = await DetectionProcess.RunAsync(smi, ["--query-gpu=name,driver_version,compute_cap", "--format=csv,noheader,nounits"], cancellationToken);
            if (response is not null)
                foreach (string line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var cells = line.Trim().Split(',').Select(s => s.Trim()).ToArray();
                    if (cells.Length < 3) continue;
                    double? cap = double.TryParse(cells[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
                    string name = string.Join(", ", cells[..^2]);
                    bool eligible = cap is 7.5 or 8.6;
                    gpus.Add(new(name, cells[^2], cap, eligible));
                }
        }
        // Include integrated/other adapters as well, and provide a fallback on systems where nvidia-smi is unavailable.
        if (OperatingSystem.IsWindows())
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            const string query = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion) | ConvertTo-Json -Compress";
            string? json = await DetectionProcess.RunAsync(powershell, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", query], cancellationToken);
            if (json is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var entries = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : [doc.RootElement];
                    foreach (var entry in entries)
                    {
                        string name = entry.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name) || gpus.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                        string driver = entry.TryGetProperty("DriverVersion", out var d) ? d.GetString() ?? "" : "";
                        bool nvidia = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
                        if (nvidia && Version.TryParse(driver, out var winDriver) && winDriver.Build >= 0 && winDriver.Revision >= 0)
                        {
                            int raw = (winDriver.Build % 10) * 10000 + winDriver.Revision;
                            driver = (raw / 100.0).ToString("F2", CultureInfo.InvariantCulture);
                        }
                        bool eligible = nvidia && Regex.IsMatch(name, @"\bRTX\s+(20|30)\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        gpus.Add(new(name, driver, null, eligible));
                    }
                }
                catch (JsonException) { checks.Add(new("gpu_query", CheckLevel.Warning)); }
            }
        }
        var eligibleGpus = gpus.Where(g => g.Eligible).ToArray();
        checks.Add(new("gpu_supported", eligibleGpus.Length > 0 ? CheckLevel.Pass : CheckLevel.Blocked, string.Join(" / ", gpus.Select(g => g.Name))));
        if (eligibleGpus.Any(g => g.ComputeCapability is null)) checks.Add(new("gpu_architecture_unverified", CheckLevel.Warning));
        if (gpus.Any(g => Regex.IsMatch(g.Name, @"\bRTX\s+(40|50)\d{2}\b", RegexOptions.IgnoreCase))) checks.Add(new("gpu_native_fg", CheckLevel.Info));
        foreach (var gpu in eligibleGpus)
        {
            bool knownDriver = int.TryParse(gpu.DriverVersion.Split('.')[0], out int branch);
            checks.Add(new("driver_r580", knownDriver && branch >= 580 ? CheckLevel.Pass : CheckLevel.Blocked, $"{gpu.Name} · {gpu.DriverVersion}"));
        }
        if (gpus.Count > 1) checks.Add(new("gpu_multiple", CheckLevel.Warning));
        bool? hagsConfigured = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                hagsConfigured = key?.GetValue("HwSchMode") switch { 2 => true, 1 => false, _ => null };
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        checks.Add(new("hags_configured", hagsConfigured == true ? CheckLevel.Pass : hagsConfigured == false ? CheckLevel.Blocked : CheckLevel.Unknown));
        checks.Add(new("hags_runtime", CheckLevel.Warning));
        return new(gpus, supportedOs, hagsConfigured, null, checks);
    }

    private static string FindNvidiaSmi()
    {
        if (!OperatingSystem.IsWindows()) return "";
        var paths = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };
        return paths.FirstOrDefault(File.Exists) ?? "";
    }
}

internal static class DetectionProcess
{
    internal static async Task<string?> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string text = await output;
            await error;
            return process.ExitCode == 0 && text.Length <= 1_048_576 ? text.Trim('\uFEFF', ' ', '\r', '\n') : null;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }
}

public sealed class GameScanner
{
    private static readonly string[] Proxies = ["version.dll", "winmm.dll", "dbghelp.dll", "dinput8.dll", "dxgi.dll", "d3d12.dll"];
    private const int MaxEntries = 50000;
    private const int MaxDepth = 24;

    public Task<GameScan> ScanAsync(string folderOrExe, ReleasePackage? package = null, string? renderingExe = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(folderOrExe, package, renderingExe, cancellationToken), cancellationToken);

    private static GameScan Scan(string input, ReleasePackage? package, string? chosenExe, CancellationToken token)
    {
        var checks = new List<CheckResult>();
        var candidates = new List<ExeCandidate>();
        string root;
        try { root = Path.GetFullPath(input.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return Empty(input, "game_path", checks); }
        if (File.Exists(root))
        {
            if (!Path.GetExtension(root).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Empty(root, "game_path", checks);
            chosenExe ??= root;
            root = Path.GetDirectoryName(root)!;
        }
        if (Path.GetFileName(root).Equals("Win64", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileName(Path.GetDirectoryName(root)), "Binaries", StringComparison.OrdinalIgnoreCase))
            root = Path.GetDirectoryName(Path.GetDirectoryName(root)!)!;
        if (!Directory.Exists(root) || Path.GetPathRoot(root)?.Equals(root, StringComparison.OrdinalIgnoreCase) == true)
            return Empty(root, "game_path", checks);
        try { if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return Empty(root, "game_path", checks); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Empty(root, "game_path", checks); }
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && DetectionPaths.IsWithin(root, windows)) return Empty(root, "game_path", checks);
        var files = new List<string>();
        var dirs = new Stack<(string Path, int Depth)>();
        dirs.Push((root, 0));
        var scanErrors = new List<string>();
        bool antiCheat = false;
        int entries = 0;
        while (dirs.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = dirs.Pop();
            string[] children;
            try { children = Directory.GetFileSystemEntries(dir.Path); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { scanErrors.Add(dir.Path); continue; }
            foreach (string child in children)
            {
                token.ThrowIfCancellationRequested();
                if (++entries > MaxEntries) { scanErrors.Add(root); dirs.Clear(); break; }
                string name = Path.GetFileName(child);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { scanErrors.Add(child); continue; }
                if (IsAntiCheat(name)) antiCheat = true;
                if ((attributes & FileAttributes.ReparsePoint) != 0) { scanErrors.Add(child); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (name.Equals("dlssg_sm86", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".dlssg-manager", StringComparison.OrdinalIgnoreCase)) continue;
                    if (dir.Depth >= MaxDepth) scanErrors.Add(child);
                    else dirs.Push((child, dir.Depth + 1));
                }
                else files.Add(child);
            }
        }
        checks.Add(new("game_scan", scanErrors.Count == 0 ? CheckLevel.Pass : CheckLevel.Blocked, scanErrors.Count == 0 ? root : string.Join(Environment.NewLine, scanErrors.Take(5))));
        foreach (string path in files.Where(p => Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(path);
            if (Regex.IsMatch(path, @"(?:[\\/](?:Redist|Redistributables|Prerequisites|Installer|Extras)[\\/])|(?:crashpad|crashreport|sentry\.|vc_redist|vcredist|unins\d*|ue4prereq|ueprereq|easyanticheat|battleye)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            int score = name.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ? 100 : 0;
            if (path.Contains(@"\Binaries\Win64\", StringComparison.OrdinalIgnoreCase)) score += 10;
            if (Path.GetDirectoryName(path)!.Equals(root, StringComparison.OrdinalIgnoreCase)) score += 20;
            if (name.Contains("Launcher", StringComparison.OrdinalIgnoreCase)) score -= 50;
            candidates.Add(new(path, name, score));
        }
        candidates = candidates.OrderByDescending(e => e.Score).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
        string? exe = null;
        if (!string.IsNullOrWhiteSpace(chosenExe))
        {
            try
            {
                string full = Path.GetFullPath(chosenExe);
                if (DetectionPaths.IsWithin(full, root) && candidates.Any(c => c.Path.Equals(full, StringComparison.OrdinalIgnoreCase))) exe = full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        else if (candidates.Count == 1) exe = candidates[0].Path;
        else if (candidates.Count(c => c.Score >= 100) == 1) exe = candidates.Single(c => c.Score >= 100).Path;
        checks.Add(new("render_exe", exe is null ? CheckLevel.Blocked : CheckLevel.Pass, exe ?? string.Join(Environment.NewLine, candidates.Select(c => c.Path))));
        bool x64 = false, directDx12 = false;
        if (exe is not null)
        {
            try
            {
                var pe = DetectionPe.Read(exe);
                x64 = pe.X64;
                directDx12 = pe.Imports.Any(i => i.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException) { }
            checks.Add(new("exe_x64", x64 ? CheckLevel.Pass : CheckLevel.Blocked, exe));
        }
        var runtimes = files.Where(p => Path.GetFileName(p).Equals("nvngx_dlssg.dll", StringComparison.OrdinalIgnoreCase)).Select(p => (Path: p, Version: ReadVersion(p))).ToArray();
        var runtime = runtimes.OrderByDescending(r => r.Path.Contains("Streamline", StringComparison.OrdinalIgnoreCase)).ThenByDescending(r => r.Version?.StartsWith("310.", StringComparison.Ordinal) == true).FirstOrDefault();
        bool nativeRuntime = runtime.Path is not null;
        checks.Add(new("game_dlssg", nativeRuntime ? CheckLevel.Pass : CheckLevel.Blocked, runtime.Path ?? ""));
        if (nativeRuntime) checks.Add(new("runtime_generation", runtime.Version?.StartsWith("310.", StringComparison.Ordinal) == true ? CheckLevel.Pass : CheckLevel.Blocked, runtime.Version ?? "?"));
        if (runtimes.Select(r => r.Version).Distinct().Count() > 1) checks.Add(new("runtime_multiple", CheckLevel.Warning, string.Join(Environment.NewLine, runtimes.Select(r => $"{r.Path} · {r.Version}"))));
        bool dx12Evidence = directDx12 || files.Any(p => Path.GetFileName(p).Equals("D3D12Core.dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Equals("sl.interposer.dll", StringComparison.OrdinalIgnoreCase));
        checks.Add(new(dx12Evidence ? "dx12_evidence" : "dx12_unverified", dx12Evidence ? CheckLevel.Info : CheckLevel.Warning));
        checks.Add(new("anticheat", antiCheat ? CheckLevel.Blocked : CheckLevel.Warning));
        string? installedVersion = null;
        ModState state = ModState.NotInstalled;
        if (exe is not null)
        {
            string target = Path.GetDirectoryName(exe)!;
            try { (state, installedVersion) = InspectMod(target, package, checks, token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                state = ModState.UnknownConflict;
                checks.Add(new("proxy_conflict", CheckLevel.Blocked, target));
            }
            bool running = IsRunning(exe, out bool uncertain);
            checks.Add(new("game_closed", running || uncertain ? CheckLevel.Blocked : CheckLevel.Pass, exe));
            string[] targetFiles = Proxies.Select(p => Path.Combine(target, p)).Where(File.Exists).ToArray();
            bool readOnly = targetFiles.Any(p => (File.GetAttributes(p) & FileAttributes.ReadOnly) != 0);
            checks.Add(new("target_readonly", readOnly ? CheckLevel.Blocked : CheckLevel.Info, target));
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(target)!);
                long required = Math.Max(300L * 1024 * 1024, (package?.Files.Sum(f => f.Size) ?? 0) * 3);
                checks.Add(new("disk_space", drive.AvailableFreeSpace >= required ? CheckLevel.Pass : CheckLevel.Blocked, $"{drive.AvailableFreeSpace / 1048576} / {required / 1048576} MiB"));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { checks.Add(new("disk_space", CheckLevel.Unknown)); }
        }
        if (package is null) checks.Add(new("package_ready", CheckLevel.Blocked));
        else checks.Add(new("package_ready", package.SupportedLayout ? CheckLevel.Pass : CheckLevel.Blocked, package.Release.Version));
        return new(root, exe, candidates, runtime.Path, runtime.Version, state, installedVersion, checks);
    }

    private static GameScan Empty(string root, string code, List<CheckResult> checks)
    {
        checks.Add(new(code, CheckLevel.Blocked, root));
        return new(root, null, [], null, null, ModState.NotInstalled, null, checks);
    }

    private static bool IsAntiCheat(string name) =>
        Regex.IsMatch(name, @"^(?:EasyAntiCheat.*|BattlEye|BEClient(?:_x64)?\.dll|BEService(?:_x64)?\.exe|.*AntiCheat.*|vgk\.sys|vgc\.exe)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? ReadVersion(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            if (v.FileMajorPart > 0) return $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    private static (ModState State, string? Version) InspectMod(string dir, ReleasePackage? package, List<CheckResult> checks, CancellationToken token)
    {
        string record = Path.Combine(dir, ".dlssg-manager.json");
        string[] present = Proxies.Where(p => File.Exists(Path.Combine(dir, p))).ToArray();
        bool modFolder = Directory.Exists(Path.Combine(dir, "dlssg_sm86"));
        if (File.Exists(record))
        {
            try
            {
                if (new FileInfo(record).Length > 2_097_152) throw new JsonException();
                using var doc = JsonDocument.Parse(File.ReadAllText(record));
                var r = doc.RootElement;
                if (!r.TryGetProperty("SchemaVersion", out var schema) || schema.GetInt32() != 1 || !r.TryGetProperty("GameDirectory", out var gameDir) || !Path.GetFullPath(gameDir.GetString()!).Equals(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) throw new JsonException();
                string proxy = r.GetProperty("Proxy").GetString()!;
                if (!Proxies.Contains(proxy, StringComparer.OrdinalIgnoreCase) || present.Any(p => !p.Equals(proxy, StringComparison.OrdinalIgnoreCase))) throw new JsonException();
                var recordFiles = r.GetProperty("Files");
                if (recordFiles.ValueKind != JsonValueKind.Array || recordFiles.GetArrayLength() == 0 || recordFiles.GetArrayLength() > 4096) throw new JsonException();
                foreach (var f in recordFiles.EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();
                    string rel = f.GetProperty("Path").GetString()!;
                    string full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (Path.IsPathRooted(rel) || !DetectionPaths.IsWithin(full, dir) || DetectionPaths.HasReparsePoint(full, dir)) throw new JsonException();
                    if (rel.Equals("dlssg_sm86.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(full) || !DetectionPaths.Hash(full).Equals(f.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    {
                        checks.Add(new("mod_modified", CheckLevel.Blocked, full));
                        return (ModState.UnknownConflict, r.GetProperty("Version").GetString());
                    }
                }
                string version = r.GetProperty("Version").GetString()!;
                checks.Add(new("mod_managed", CheckLevel.Pass, version));
                return (ModState.Managed, version);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                checks.Add(new("mod_record", CheckLevel.Blocked, record));
                return (ModState.UnknownConflict, null);
            }
        }
        if (present.Length == 0 && !modFolder && !File.Exists(Path.Combine(dir, "dlssg_sm86.ini")))
        {
            if (File.Exists(Path.Combine(dir, "dwmapi.dll"))) checks.Add(new("other_injector", CheckLevel.Warning, Path.Combine(dir, "dwmapi.dll")));
            checks.Add(new("proxy_clear", CheckLevel.Pass));
            return (ModState.NotInstalled, null);
        }
        if (present.Length == 1 && modFolder && package is not null)
        {
            string proxyRel = present[0].Equals("version.dll", StringComparison.OrdinalIgnoreCase) ? "version.dll" : "alternatives/" + present[0];
            var proxyFile = package.Files.FirstOrDefault(f => f.Path.Replace('\\', '/').Equals(proxyRel, StringComparison.OrdinalIgnoreCase));
            bool matches = proxyFile is not null && !DetectionPaths.HasReparsePoint(Path.Combine(dir, present[0]), dir) && new FileInfo(Path.Combine(dir, present[0])).Length == proxyFile.Size && DetectionPaths.Hash(Path.Combine(dir, present[0])).Equals(proxyFile.Sha256, StringComparison.OrdinalIgnoreCase);
            var required = new[] { "dlssg_sm86/backend/sm86_backend.dll", "dlssg_sm86/kernels/fg_kernels.dll", "dlssg_sm86/runtime/nvngx_dlssg.dll" };
            foreach (string rel in required)
            {
                token.ThrowIfCancellationRequested();
                var expected = package.Files.FirstOrDefault(f => f.Path.Replace('\\', '/').Equals(rel, StringComparison.OrdinalIgnoreCase));
                string target = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                matches &= expected is not null && File.Exists(target) && !DetectionPaths.HasReparsePoint(target, dir) && new FileInfo(target).Length == expected.Size && DetectionPaths.Hash(target).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            if (matches)
            {
                checks.Add(new("mod_manual", CheckLevel.Info, package.Release.Version));
                return (ModState.RecognizedManual, package.Release.Version);
            }
        }
        checks.Add(new("proxy_conflict", CheckLevel.Blocked, string.Join(Environment.NewLine, present.Select(p => Path.Combine(dir, p)).Append(Path.Combine(dir, "dlssg_sm86")))));
        return (ModState.UnknownConflict, null);
    }

    private static bool IsRunning(string exe, out bool uncertain)
    {
        uncertain = false;
        string name = Path.GetFileNameWithoutExtension(exe);
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try { if (Path.GetFullPath(p.MainModule?.FileName ?? "").Equals(Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return true; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { uncertain = true; }
            }
        }
        return false;
    }
}

internal static class DetectionPaths
{
    internal static bool HasReparsePoint(string path, string root)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null && IsWithin(current, root))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            if (current.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }
        return false;
    }
    internal static bool IsWithin(string path, string root)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase) || full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    internal static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

internal static class DetectionPe
{
    internal sealed record Info(bool X64, IReadOnlyList<string> Imports);
    internal static Info Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new BinaryReader(stream);
        if (stream.Length < 64 || r.ReadUInt16() != 0x5a4d) throw new BadImageFormatException();
        stream.Position = 0x3c;
        int pe = r.ReadInt32();
        if (pe < 64 || pe > stream.Length - 24) throw new BadImageFormatException();
        stream.Position = pe;
        if (r.ReadUInt32() != 0x00004550) throw new BadImageFormatException();
        ushort machine = r.ReadUInt16();
        ushort count = r.ReadUInt16();
        stream.Position = pe + 20;
        ushort optionalSize = r.ReadUInt16();
        stream.Position = pe + 24;
        ushort magic = r.ReadUInt16();
        bool x64 = machine == 0x8664 && magic == 0x20b;
        if (count > 96 || optionalSize < (magic == 0x20b ? 128 : 112) || pe + 24L + optionalSize + count * 40L > stream.Length) throw new BadImageFormatException();
        stream.Position = pe + 24 + (magic == 0x20b ? 120 : 104);
        uint importRva = r.ReadUInt32();
        var sections = new List<(uint Rva, uint Size, uint Raw)>();
        stream.Position = pe + 24 + optionalSize;
        for (int i = 0; i < count; i++)
        {
            stream.Position += 8;
            uint virtualSize = r.ReadUInt32(), rva = r.ReadUInt32(), rawSize = r.ReadUInt32(), raw = r.ReadUInt32();
            sections.Add((rva, Math.Max(virtualSize, rawSize), raw));
            stream.Position += 16;
        }
        long Map(uint rva)
        {
            foreach (var section in sections)
                if (rva >= section.Rva && (ulong)rva < (ulong)section.Rva + section.Size)
                {
                    long result = section.Raw + (long)rva - section.Rva;
                    if (result >= 0 && result < stream.Length) return result;
                }
            throw new BadImageFormatException();
        }
        var imports = new List<string>();
        if (importRva == 0) return new(x64, imports);
        long importOffset = Map(importRva);
        for (int i = 0; i < 256; i++)
        {
            long entry = importOffset + i * 20L;
            if (entry + 20 > stream.Length) throw new BadImageFormatException();
            stream.Position = entry;
            uint original = r.ReadUInt32(), timestamp = r.ReadUInt32(), forwarder = r.ReadUInt32(), nameRva = r.ReadUInt32(), thunk = r.ReadUInt32();
            if ((original | timestamp | forwarder | nameRva | thunk) == 0) break;
            stream.Position = Map(nameRva);
            var name = new List<byte>();
            while (name.Count < 260 && stream.Position < stream.Length)
            {
                byte b = r.ReadByte();
                if (b == 0) break;
                name.Add(b);
            }
            imports.Add(System.Text.Encoding.ASCII.GetString(name.ToArray()));
        }
        return new(x64, imports);
    }
}

public sealed class SteamLibraryService
{
    public Task<IReadOnlyList<SteamGame>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<SteamGame>>(() =>
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var current = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (current?.GetValue("SteamPath") is string value) roots.Add(value);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var steam = baseKey.OpenSubKey(@"Software\Valve\Steam");
                    if (steam?.GetValue("InstallPath") is string value) roots.Add(value);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        }
        foreach (string root in roots.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            string? text = ReadSmall(libraries);
            if (text is null) continue;
            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase))
                roots.Add(Unescape(match.Groups[1].Value));
            foreach (Match match in Regex.Matches(text, "\"[0-9]+\"\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
            {
                string value = Unescape(match.Groups[1].Value);
                if (Path.IsPathRooted(value)) roots.Add(value);
            }
        }
        var games = new Dictionary<string, SteamGame>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] manifests;
            try { manifests = Directory.GetFiles(Path.Combine(root, "steamapps"), "appmanifest_*.acf"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { continue; }
            foreach (string file in manifests.Take(5000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? text = ReadSmall(file);
                if (text is null) continue;
                string id = GetValue(text, "appid"), name = GetValue(text, "name"), install = GetValue(text, "installdir");
                if (!Regex.IsMatch(id, "^[0-9]+$") || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install) || Path.IsPathRooted(install)) continue;
                try
                {
                    string common = Path.GetFullPath(Path.Combine(root, "steamapps", "common"));
                    string directory = Path.GetFullPath(Path.Combine(common, install));
                    if (!DetectionPaths.IsWithin(directory, common) || !Directory.Exists(directory)) continue;
                    games.TryAdd(directory, new(id, name, directory));
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { }
            }
        }
        return games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, cancellationToken);

    private static string? ReadSmall(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length <= 2_097_152 ? File.ReadAllText(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    private static string GetValue(string text, string key)
    {
        var match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase);
        return match.Success ? Unescape(match.Groups[1].Value) : "";
    }
    private static string Unescape(string text) => text.Replace("\\\\", "\\").Replace("\\\"", "\"");
}

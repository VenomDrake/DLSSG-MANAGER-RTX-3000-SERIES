using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DlssgManager.Core;

/// <summary>Checks this manager's own releases; downloaded code is never started by this service.</summary>
public sealed class ManagerUpdateService : IDisposable
{
    public const string Repository = "VenomDrake/DLSSG-Mod-Manager";
    private const string LegacyRepository = "VenomDrake/DLSSG-MANAGER-RTX-3000-SERIES";
    public const string LatestReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ExeName = "DLSSGModManager.exe";
    public const string PortableAssetName = "DLSSGModManager-Portable-win-x64.zip";
    public const string InstallerAssetName = "DLSSGModManager-Setup.msi";
    public static IReadOnlyList<string> OwnedFiles { get; } = Array.AsReadOnly(new[]
    {
        ExeName, "LICENSE", "README.md", "THIRD_PARTY_NOTICES.md", "DOTNET-RUNTIME-LICENSE.txt",
        "WPF-LICENSE.txt", "DOTNET-THIRD-PARTY-NOTICES.txt"
    });
    internal const long MaximumAssetBytes = 384L * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string _cacheRoot;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _prepareLock = new(1, 1);
    private static readonly HashSet<string> TrustedHosts = new(StringComparer.OrdinalIgnoreCase)
    { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "github-releases.githubusercontent.com" };

    public ManagerUpdateService(string cacheRoot, HttpClient? client = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Retains the newest successful backup and leaves failed, incomplete and active updates alone.</summary>
    public void PruneCompletedUpdates(string currentVersion) => ManagerUpdateCleanup.Prune(_cacheRoot, currentVersion);

    /// <summary>Returns null on network/metadata failure or when no newer stable version exists.</summary>
    public async Task<ManagerUpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (!TryVersion(currentVersion, out _)) throw new ArgumentException("Invalid current manager version.", nameof(currentVersion));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await SendAsync(new Uri(LatestReleaseUrl), timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var raw = await ReadBoundedAsync(response.Content, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
            return ParseRelease(raw, currentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or FormatException
            or InvalidOperationException or OperationCanceledException or KeyNotFoundException or OverflowException)
        { return null; }
    }

    public static ManagerUpdateInfo? ParseRelease(ReadOnlyMemory<byte> bytes, string currentVersion)
    {
        if (!TryVersion(currentVersion, out var current)) throw new ArgumentException("Invalid current manager version.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)
            || !TryVersion(tag, out var next) || next <= current) return null;
        var releaseUrl = root.GetProperty("html_url").GetString() ?? "";
        if (!ValidRepositoryUrl(releaseUrl, "/releases/tag/" + tag)) return null;
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        ManagerUpdateAsset? Asset(string name)
        {
            var matching = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (matching.Length != 1) return null;
            var a = matching[0];
            var hash = a.TryGetProperty("digest", out var h) ? h.GetString() ?? "" : "";
            var size = a.GetProperty("size").GetInt64();
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (!hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !IsHash(hash[7..])
                || size <= 0 || size > MaximumAssetBytes || !ValidAssetUrl(url, tag, name)) return null;
            return new(name, url, size, hash[7..].ToLowerInvariant());
        }
        var portable = Asset(PortableAssetName);
        var installer = Asset(InstallerAssetName);
        return portable is null || installer is null ? null : new(next.ToString(3), tag, releaseUrl, portable, installer);
    }

    public async Task<ManagerUpdatePlan> PrepareAsync(ManagerUpdateInfo info, string currentExe, string dataDirectory,
        int parentProcessId, IReadOnlyList<string> launchArguments, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        await _prepareLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? operation = null;
        try
        {
            var data = Path.GetFullPath(dataDirectory);
            if (!SamePath(_cacheRoot, Path.Combine(data, "manager-updates")))
                throw new InvalidOperationException("The update cache must be under the manager's data directory.");
            var target = Path.GetFullPath(currentExe);
            if (!SamePath(target, Environment.ProcessPath ?? "") || parentProcessId != Environment.ProcessId)
                throw new InvalidOperationException("Only the currently running manager can be updated.");
            ValidateTargetPath(target);
            var current = ReadManagerVersion(target);
            ValidateInfo(info);
            if (!TryVersion(info.Version, out var to) || to <= current)
                throw new InvalidOperationException("The downloaded manager must be newer than the running version.");
            ValidateLaunchArguments(launchArguments, data);
            EnsureSafeDirectory(_cacheRoot);
            operation = Path.Combine(_cacheRoot, "update-" + Guid.NewGuid().ToString("N"));
            EnsureSafeDirectory(operation);
            var candidate = Path.Combine(operation, "candidate");
            EnsureSafeDirectory(candidate);
            var mode = DetectMode(target);
            var asset = mode == ManagerUpdateMode.Installed ? info.InstallerAsset : info.PortableAsset;
            var archive = Path.Combine(operation, asset.Name);
            EnsureSpace(operation, checked(asset.Size * 3 + new FileInfo(target).Length * 2 + 16L * 1024 * 1024));
            await DownloadAsync(asset, archive, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new("self_update_verifying"));
            IReadOnlyList<ManagerUpdateFile> files;
            if (mode == ManagerUpdateMode.Portable)
            {
                files = await ExtractPortableAsync(archive, candidate, cancellationToken).ConfigureAwait(false);
                if (ReadManagerVersion(Path.Combine(candidate, ExeName)) != to)
                    throw new InvalidDataException("The executable version does not match the update release.");
                EnsureSpace(Path.GetDirectoryName(target)!, checked(files.Sum(f => f.Size) * 3 + 16L * 1024 * 1024));
            }
            else
            {
                var destination = Path.Combine(candidate, InstallerAssetName);
                File.Move(archive, destination);
                ManagerUpdateMsi.Validate(destination, info.Version);
                files = [new(InstallerAssetName, asset.Size, asset.Sha256)];
            }
            progress?.Report(new("self_update_preparing"));
            var workerDir = Path.Combine(operation, "worker");
            EnsureSafeDirectory(workerDir);
            var worker = Path.Combine(workerDir, ExeName);
            var originalHash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
            await CopyAsync(target, worker, cancellationToken).ConfigureAwait(false);
            if (!HashEquals(originalHash, await HashAsync(worker, cancellationToken).ConfigureAwait(false)))
                throw new InvalidDataException("The updater worker copy could not be verified.");
            using var process = Process.GetCurrentProcess();
            var plan = new ManagerUpdatePlan
            {
                DataDirectory = data, OperationDirectory = operation, CandidateDirectory = candidate,
                PlanPath = Path.Combine(operation, "update-plan.json"), ResultPath = Path.Combine(operation, "update-result.json"),
                WorkerExePath = worker, WorkerSha256 = originalHash, TargetExePath = target,
                OriginalExeSha256 = originalHash, FromVersion = current.ToString(3), ToVersion = to.ToString(3),
                ParentProcessId = parentProcessId, ParentStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                Mode = mode, Files = files, LaunchArguments = launchArguments.ToArray()
            };
            var raw = JsonSerializer.SerializeToUtf8Bytes(plan, Json);
            if (raw.Length > 65536) throw new InvalidDataException("The update plan is too large.");
            await using (var planStream = new FileStream(plan.PlanPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await planStream.WriteAsync(raw, cancellationToken).ConfigureAwait(false);
                await planStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            return plan with { PlanSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() };
        }
        catch
        {
            // Every created path is isolated beneath the generated operation directory.
            if (operation is not null) TryRemoveOperation(_cacheRoot, operation);
            throw;
        }
        finally { _prepareLock.Release(); }
    }

    internal static void ValidateInfo(ManagerUpdateInfo info)
    {
        if (info.Tag is null || !Regex.IsMatch(info.Tag, @"\Av?\d+\.\d+\.\d+\z", RegexOptions.CultureInvariant)
            || !TryVersion(info.Version, out var version) || !TryVersion(info.Tag, out var tagVersion) || version != tagVersion
            || !ValidRepositoryUrl(info.ReleaseUrl, "/releases/tag/" + info.Tag)
            || info.PortableAsset is null || info.InstallerAsset is null) throw new InvalidDataException("Invalid manager update metadata.");
        foreach (var pair in new[] { (info.PortableAsset, PortableAssetName), (info.InstallerAsset, InstallerAssetName) })
            if (pair.Item1.Name != pair.Item2 || !ValidAssetUrl(pair.Item1.DownloadUrl, info.Tag, pair.Item2)
                || pair.Item1.Size <= 0 || pair.Item1.Size > MaximumAssetBytes || !IsHash(pair.Item1.Sha256))
                throw new InvalidDataException("Invalid manager release asset.");
    }

    private async Task DownloadAsync(ManagerUpdateAsset asset, string destination, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await SendAsync(new Uri(asset.DownloadUrl), timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != asset.Size) throw new InvalidDataException("Update download size mismatch.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920]; long count = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
                if (count > asset.Size || count > MaximumAssetBytes) throw new InvalidDataException("Update download exceeded its declared size.");
                await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                progress?.Report(new("self_update_downloading", count * 100.0 / asset.Size));
            }
            if (count != asset.Size) throw new InvalidDataException("Incomplete update download.");
            await target.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        if (!HashEquals(await HashAsync(destination, timeout.Token).ConfigureAwait(false), asset.Sha256))
            throw new InvalidDataException("The manager update SHA-256 does not match GitHub metadata.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri initial, CancellationToken token)
    {
        var uri = initial;
        for (int hop = 0; hop < 6; hop++)
        {
            ValidateTrustedUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("DLSSG-Mod-Manager/0.2");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } final) ValidateTrustedUri(final);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("Update redirect has no destination.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            return response;
        }
        throw new InvalidDataException("Too many update download redirects.");
    }

    internal static async Task<IReadOnlyList<ManagerUpdateFile>> ExtractPortableAsync(string archivePath, string destination, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count != OwnedFiles.Count) throw new InvalidDataException("Unexpected portable update layout.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = OwnedFiles.FirstOrDefault(n => n.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase));
            if (name is null || !seen.Add(name) || entry.Length < 0 || entry.Length > MaximumAssetBytes
                || (total = checked(total + entry.Length)) > MaximumAssetBytes
                || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0
                || entry.Length > 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 1000)
                throw new InvalidDataException("The portable update contains an unsafe or unexpected entry.");
        }
        EnsureSafeDirectory(destination);
        var files = new List<ManagerUpdateFile>();
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = OwnedFiles.Single(n => n.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase));
            var target = Path.Combine(destination, name);
            await using var input = entry.Open();
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long count = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                    if (count > entry.Length) throw new InvalidDataException("Portable update entry exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }
                if (count != entry.Length) throw new InvalidDataException("Portable update entry size mismatch.");
            }
            files.Add(new(name, entry.Length, await HashAsync(target, token).ConfigureAwait(false)));
        }
        return files;
    }

    internal static bool TryVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim().TrimStart('v', 'V').Split('+')[0];
        if (!Regex.IsMatch(value, @"^\d+\.\d+\.\d+(\.\d+)?$", RegexOptions.CultureInvariant)
            || !Version.TryParse(value, out var parsed) || parsed.Build < 0) return false;
        version = new Version(parsed.Major, parsed.Minor, parsed.Build);
        return true;
    }
    internal static Version ReadManagerVersion(string path)
    {
        RejectReparsePath(path);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64)
            throw new InvalidDataException("The manager executable is not x64.");
        var info = FileVersionInfo.GetVersionInfo(path);
        if (!string.Equals(info.ProductName, "DLSSG Mod Manager", StringComparison.Ordinal)
            || !TryVersion(info.FileVersion?.Replace(',', '.'), out var version))
            throw new InvalidDataException("The executable is not a recognized DLSSG Mod Manager build.");
        return version;
    }
    internal static ManagerUpdateMode DetectMode(string currentExe)
    {
        if (!OperatingSystem.IsWindows()) return ManagerUpdateMode.Portable;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\DLSSGModManager");
        return key?.GetValue("Installed") is int installed && installed == 1 && key.GetValue("InstallLocation") is string location
            && SamePath(location, Path.GetDirectoryName(currentExe)!) ? ManagerUpdateMode.Installed : ManagerUpdateMode.Portable;
    }
    internal static void ValidateTargetPath(string path)
    {
        if (!Path.GetFileName(path).Equals(ExeName, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("The manager target executable is invalid.");
        var dir = Path.GetDirectoryName(path)!;
        if (SamePath(dir, Path.GetPathRoot(dir) ?? "")) throw new InvalidDataException("An update cannot target a drive root.");
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var protectedDirectory = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(protectedDirectory) && IsWithin(protectedDirectory, path))
                throw new InvalidDataException("The manager cannot update itself inside a protected Windows directory.");
        }
        RejectReparsePath(path);
    }
    internal static void ValidateLaunchArguments(IReadOnlyList<string> arguments, string dataDirectory)
    {
        if (arguments is null || arguments.Count > 8 || arguments.Any(a => a is null || a.Length > 32768)) throw new InvalidDataException("Invalid manager restart arguments.");
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == "--read-only") continue;
            if (arguments[i] is not ("--game" or "--data-dir") || ++i >= arguments.Count) throw new InvalidDataException("Unknown manager restart argument.");
            if (arguments[i - 1] == "--data-dir" && !SamePath(arguments[i], dataDirectory)) throw new InvalidDataException("The restart data directory differs from the update plan.");
        }
    }
    internal static bool IsHash(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    internal static bool HashEquals(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    internal static async Task<string> HashAsync(string file, CancellationToken token)
    {
        RejectReparsePath(file);
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
    internal static async Task CopyAsync(string source, string target, CancellationToken token)
    {
        RejectReparsePath(source); RejectReparsePath(Path.GetDirectoryName(target)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }
    internal static void EnsureSafeDirectory(string path) { RejectReparsePath(path); Directory.CreateDirectory(path); RejectReparsePath(path); }
    internal static void RejectReparsePath(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Update paths cannot contain symbolic links or reparse points.");
        }
    }
    internal static bool SamePath(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    internal static bool IsWithin(string parent, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static void EnsureSpace(string path, long bytes)
    { if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace < bytes) throw new IOException("Not enough free disk space for the manager update and backup."); }
    private static bool ValidAssetUrl(string url, string tag, string name) => ValidRepositoryUrl(url, "/releases/download/" + tag + "/" + name);
    private static bool ValidRepositoryUrl(string url, string releasePath) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && uri.UserInfo.Length == 0
        // Only this owner's two exact repository names identify the same renamed project.
        && (uri.AbsolutePath == "/" + Repository + releasePath || uri.AbsolutePath == "/" + LegacyRepository + releasePath)
        && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    private static void ValidateTrustedUri(Uri uri)
    { if (uri.Scheme != "https" || !TrustedHosts.Contains(uri.Host) || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) throw new InvalidDataException("Untrusted manager update destination."); }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        await using var source = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var target = new MemoryStream(); var buffer = new byte[8192];
        while (true) { var count = await source.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break; if (target.Length + count > maximum) throw new InvalidDataException("Update metadata is too large."); await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
        return target.ToArray();
    }
    private static void TryRemoveOperation(string parent, string operation)
    {
        try { if (IsWithin(parent, operation) && Path.GetFileName(operation).StartsWith("update-", StringComparison.Ordinal)) { RejectReparsePath(operation); Directory.Delete(operation, true); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    // A closing window can cancel a preparation still unwinding its finally block.
    // This semaphore never exposes its wait handle and can be reclaimed normally by the GC.
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

/// <summary>Downloads and verifies data from the upstream release; never loads downloaded code.</summary>
public sealed class ReleaseService : IDisposable
{
    public const string RepositoryUrl = "https://github.com/sdli1995/dlssg_for_sm86";
    public const string LatestReleaseUrl = "https://api.github.com/repos/sdli1995/dlssg_for_sm86/releases/latest";
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    public const int MaximumEntries = 4096;
    private const int MaximumMetadataBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly string[] RequiredFiles =
    [
        "version.dll", "dlssg_sm86.ini", "dlssg_sm86/backend/sm86_backend.dll",
        "dlssg_sm86/kernels/fg_kernels.dll", "dlssg_sm86/runtime/nvngx_dlssg.dll",
        "dlssg_sm86/sr/nvngx_dlss.dll", "dlssg_sm86/sr/sr_kernels.dll",
        "dlssg_sm86/xefg/xefg_kernels.dll", "LICENSE.md", "THIRD_PARTY_NOTICES.txt"
    ];
    private static readonly HashSet<string> RedirectHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com", "api.github.com", "release-assets.githubusercontent.com",
        "objects.githubusercontent.com", "github-releases.githubusercontent.com"
    };
    private readonly string _cacheRoot;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    /// <remarks>An injected HttpClient should have automatic redirects disabled. Final URLs are checked as well.</remarks>
    public ReleaseService(string cacheRoot, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<UpdateResult> EnsureLatestAsync(IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        ReleasePackage? cached = null;
        try
        {
            EnsureSafeDirectory(_cacheRoot);
            progress?.Report(new("checking_update"));
            cached = await LoadCachedAsync(cancellationToken).ConfigureAwait(false);
            ReleaseInfo release;
            try
            {
                using var metadataTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                metadataTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await SendTrustedAsync(new Uri(LatestReleaseUrl), metadataTimeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await ReadLimitedAsync(response.Content, MaximumMetadataBytes, metadataTimeout.Token).ConfigureAwait(false);
                release = ParseLatestRelease(bytes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
            {
                return new(cached is null ? UpdateState.Failed : UpdateState.OfflineCache, cached, SafeError(error));
            }

            if (cached is not null && SameAsset(cached.Release, release))
            {
                progress?.Report(new("up_to_date", 100, release.Version));
                return new(UpdateState.UpToDate, cached);
            }

            stage = Path.Combine(_cacheRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            EnsureSafeDirectory(stage);
            var zipPath = Path.Combine(stage, "release.zip");
            using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                downloadTimeout.CancelAfter(TimeSpan.FromMinutes(10));
                try
                {
                    await DownloadAsync(release, zipPath, progress, downloadTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
                {
                    return new(cached is null ? UpdateState.Failed : UpdateState.OfflineCache, cached, SafeError(error));
                }
            }
            progress?.Report(new("verifying_package", null, release.Version));
            var archiveHash = await HashFileAsync(zipPath, cancellationToken).ConfigureAwait(false);
            if (!archiveHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release archive SHA-256 does not match GitHub metadata.");

            var extractedPath = Path.Combine(stage, "package");
            await ExtractVerifiedArchiveAsync(zipPath, extractedPath, cancellationToken).ConfigureAwait(false);
            var package = await ValidatePackageAsync(extractedPath, release, cancellationToken).ConfigureAwait(false);
            var manifestHash = await HashFileAsync(Path.Combine(extractedPath, "manifest.json"), cancellationToken).ConfigureAwait(false);
            var key = $"{release.ReleaseId}-{release.AssetId}-{release.Sha256[..16].ToLowerInvariant()}";
            var packagesPath = Path.Combine(_cacheRoot, "packages");
            EnsureSafeDirectory(packagesPath);
            var destination = Path.Combine(packagesPath, key);
            if (Directory.Exists(destination))
            {
                // Preserve a damaged/old cache instead of overwriting files that may be in use.
                RejectReparsePoint(destination);
                Directory.Move(destination, destination + ".previous-" + Guid.NewGuid().ToString("N"));
            }
            Directory.Move(stage, destination);
            stage = null;
            var cacheState = new CacheState(release, key, manifestHash);
            var statePath = Path.Combine(_cacheRoot, "current.json");
            var tempState = Path.Combine(_cacheRoot, ".current-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await File.WriteAllTextAsync(tempState, JsonSerializer.Serialize(cacheState, JsonOptions), cancellationToken).ConfigureAwait(false);
                File.Move(tempState, statePath, true);
            }
            finally { if (File.Exists(tempState)) File.Delete(tempState); }
            package = package with { Directory = Path.Combine(destination, "package") };
            progress?.Report(new("download_complete", 100, release.Version));
            return new(UpdateState.Downloaded, package);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or CryptographicException or InvalidOperationException or ArgumentException
            or KeyNotFoundException or FormatException)
        {
            return new(UpdateState.Failed, cached, SafeError(error));
        }
        finally
        {
            if (stage is not null) TryDeleteStage(stage);
            _operationLock.Release();
        }
    }

    /// <summary>Returns a cache only after rechecking the archive, manifest and every packaged file.</summary>
    public async Task<ReleasePackage?> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var currentPath = Path.Combine(_cacheRoot, "current.json");
            if (!File.Exists(currentPath)) return null;
            RejectReparsePoint(_cacheRoot);
            RejectReparsePoint(currentPath);
            if (new FileInfo(currentPath).Length > MaximumMetadataBytes) return null;
            var state = JsonSerializer.Deserialize<CacheState>(await File.ReadAllTextAsync(currentPath, cancellationToken).ConfigureAwait(false), JsonOptions);
            if (state is null || state.Release is null || !Regex.IsMatch(state.PackageKey, @"^\d+-\d+-[a-f0-9]{16}$", RegexOptions.CultureInvariant)
                || !IsSha256(state.ManifestSha256) || !IsSha256(state.Release.Sha256)) return null;
            ValidateReleaseUrls(state.Release);
            var packageContainer = Path.Combine(_cacheRoot, "packages", state.PackageKey);
            RejectReparsePoint(Path.Combine(_cacheRoot, "packages"));
            RejectReparsePoint(packageContainer);
            var archive = Path.Combine(packageContainer, "release.zip");
            RejectReparsePoint(archive);
            if (!File.Exists(archive) || new FileInfo(archive).Length != state.Release.Size
                || !(await HashFileAsync(archive, cancellationToken).ConfigureAwait(false)).Equals(state.Release.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            var directory = Path.Combine(packageContainer, "package");
            var manifestPath = Path.Combine(directory, "manifest.json");
            RejectReparsePoint(directory);
            RejectReparsePoint(manifestPath);
            if (!File.Exists(manifestPath) || !(await HashFileAsync(manifestPath, cancellationToken).ConfigureAwait(false))
                .Equals(state.ManifestSha256, StringComparison.OrdinalIgnoreCase)) return null;
            return await ValidatePackageAsync(directory, state.Release, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or CryptographicException or ArgumentException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    public async Task<ReleasePackage> ValidatePackageAsync(string directory, ReleaseInfo release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var root = Path.GetFullPath(directory);
        RejectReparsePoint(root);
        var manifestPath = Path.Combine(root, "manifest.json");
        RejectReparsePoint(manifestPath);
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > MaximumMetadataBytes)
            throw new InvalidDataException("The complete release manifest is missing or too large.");
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        var manifest = document.RootElement;
        if (!manifest.TryGetProperty("project", out var project) || project.GetString() != "dlssg"
            || !manifest.TryGetProperty("bundle", out var bundle)
            || !bundle.TryGetProperty("version", out var bundleVersion)
            || !NormalizeVersion(bundleVersion.GetString() ?? "").Equals(NormalizeVersion(release.Version), StringComparison.OrdinalIgnoreCase)
            || !manifest.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array
            || filesElement.GetArrayLength() is 0 or > MaximumEntries)
            throw new InvalidDataException("The release manifest does not match this release.");

        var files = new List<PackageFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;
        foreach (var element in filesElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = element.GetProperty("path").GetString() ?? "";
            var fullPath = ResolveSafePackagePath(root, relativePath);
            relativePath = relativePath.Replace('\\', '/');
            if (!names.Add(relativePath) || relativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release manifest has duplicate or recursive entries.");
            var size = element.GetProperty("size").GetInt64();
            var sha = element.GetProperty("sha256").GetString() ?? "";
            if (size < 0 || size > MaximumExpandedBytes || !IsSha256(sha)
                || (totalSize = checked(totalSize + size)) > MaximumExpandedBytes)
                throw new InvalidDataException("The release manifest has invalid file sizes or hashes.");
            RejectReparseAncestors(root, fullPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length != size
                || !(await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Packaged file verification failed: " + relativePath);
            files.Add(new(relativePath, size, sha.ToLowerInvariant()));
        }
        foreach (var actualFile in EnumerateSafeFiles(root, cancellationToken))
        {
            var relative = Path.GetRelativePath(root, actualFile).Replace('\\', '/');
            if (!relative.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) && !names.Contains(relative))
                throw new InvalidDataException("An unlisted file is present in the package: " + relative);
        }
        // Downloading the latest release is independent of knowing how to install it.
        // The R580 floor and INI recipe are validated for this exact runtime/release;
        // sharing a minor version does not make a future release compatible.
        var supportedRecipe = NormalizeVersion(release.Version) == "0.4.1"
            && bundle.TryGetProperty("runtime_model", out var runtimeModel)
            && runtimeModel.ValueKind == JsonValueKind.String && runtimeModel.GetString() == "310.9.16"
            && bundle.TryGetProperty("runtime_version", out var runtimeVersion)
            && runtimeVersion.ValueKind == JsonValueKind.String && runtimeVersion.GetString() == "310.9.16.0";
        var supportedLayout = supportedRecipe && RequiredFiles.All(names.Contains);
        return new(release, root, files, supportedLayout,
        [
            new("package_hash", CheckLevel.Pass, release.Sha256),
            new("package_layout", supportedLayout ? CheckLevel.Pass : CheckLevel.Blocked, release.Version)
        ]);
    }

    /// <summary>Parses the official latest-release response. Public for deterministic tests.</summary>
    public static ReleaseInfo ParseLatestRelease(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("A draft or prerelease cannot be used as the latest stable package.");
        var assets = root.GetProperty("assets").EnumerateArray().Where(asset =>
            Regex.IsMatch(asset.GetProperty("name").GetString() ?? "", @"^dlssg-release-[A-Za-z0-9._-]+-x64\.zip$", RegexOptions.CultureInvariant)).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("A unique complete x64 release package was not found.");
        var asset = assets[0];
        var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !IsSha256(digest[7..]))
            throw new InvalidDataException("GitHub did not publish a SHA-256 digest for this release asset.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumArchiveBytes) throw new InvalidDataException("The archive size is outside the accepted limits.");
        var release = new ReleaseInfo(root.GetProperty("tag_name").GetString() ?? "",
            root.GetProperty("id").GetInt64(), asset.GetProperty("id").GetInt64(),
            asset.GetProperty("name").GetString()!, asset.GetProperty("browser_download_url").GetString()!,
            size, digest[7..].ToLowerInvariant(), root.GetProperty("published_at").GetDateTimeOffset(),
            root.GetProperty("html_url").GetString()!);
        if (string.IsNullOrWhiteSpace(release.Version) || release.ReleaseId <= 0 || release.AssetId <= 0)
            throw new InvalidDataException("The release identity is invalid.");
        ValidateReleaseUrls(release);
        return release;
    }

    public static bool SameAsset(ReleaseInfo left, ReleaseInfo right) =>
        left.ReleaseId == right.ReleaseId && left.AssetId == right.AssetId && left.Size == right.Size
        && left.Version.Equals(right.Version, StringComparison.OrdinalIgnoreCase)
        && left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a package-relative Windows-safe filename, rejecting traversal and alternate streams.</summary>
    public static string ResolveSafePackagePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 512 || relativePath.Contains('\0')
            || relativePath.StartsWith('/') || relativePath.StartsWith('\\') || relativePath.Contains(':'))
            throw new InvalidDataException("Unsafe package path.");
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."
            || segment.EndsWith(' ') || segment.EndsWith('.') || segment.Any(c => c < 32 || "<>\"|?*".Contains(c))
            || Regex.IsMatch(segment.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new InvalidDataException("Unsafe package path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package entry is outside its destination.");
        return fullPath;
    }

    /// <summary>Extracts an archive into a new directory after checking all entries, before creating any files.</summary>
    public static async Task ExtractVerifiedArchiveAsync(string zipPath, string destination,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidDataException("The extraction destination must be new.");
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count is 0 or > MaximumEntries) throw new InvalidDataException("Invalid archive entry count.");
        var entries = new List<(ZipArchiveEntry Entry, string Target, bool Directory)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var name = isDirectory ? entry.FullName[..^1] : entry.FullName;
            var target = ResolveSafePackagePath(destination, name);
            if (!names.Add(name.Replace('\\', '/')) || (entry.ExternalAttributes >> 16 & 0xF000) is 0xA000
                || ((FileAttributes)(entry.ExternalAttributes & 0xFFFF) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The archive contains a duplicate path or symbolic link.");
            if (entry.Length < 0 || entry.Length > MaximumExpandedBytes
                || (expanded = checked(expanded + entry.Length)) > MaximumExpandedBytes
                || (entry.Length > 10L * 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000)
                || (isDirectory && entry.Length != 0))
                throw new InvalidDataException("The expanded archive exceeds the accepted limits.");
            entries.Add((entry, target, isDirectory));
        }
        // Reject a file that is also another entry's parent before extraction begins.
        var fileTargets = new HashSet<string>(entries.Where(e => !e.Directory).Select(e => e.Target), StringComparer.OrdinalIgnoreCase);
        foreach (var item in entries)
        {
            for (var parent = Path.GetDirectoryName(item.Target); parent is not null
                && parent.Length >= Path.GetFullPath(destination).Length; parent = Path.GetDirectoryName(parent))
                if (fileTargets.Contains(parent)) throw new InvalidDataException("A file conflicts with a directory in this archive.");
        }
        Directory.CreateDirectory(destination);
        RejectReparsePoint(destination);
        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Directory) { EnsureSafeDirectory(item.Target); continue; }
            EnsureSafeDirectory(Path.GetDirectoryName(item.Target)!);
            RejectReparseAncestors(destination, item.Target);
            await using var input = item.Entry.Open();
            await using var output = new FileStream(item.Target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualBytes = await CopyLimitedAsync(input, output, item.Entry.Length, cancellationToken).ConfigureAwait(false);
            if (actualBytes != item.Entry.Length) throw new InvalidDataException("An archive entry has an unexpected expanded size.");
        }
    }

    private async Task DownloadAsync(ReleaseInfo release, string destination, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        using var response = await SendTrustedAsync(new Uri(release.DownloadUrl), token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength != release.Size)
            throw new InvalidDataException("The release download size does not match GitHub metadata.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long received = 0;
        var lastReport = Environment.TickCount64;
        progress?.Report(new("downloading_package", 0, release.Version));
        while (true)
        {
            var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) break;
            received += count;
            if (received > release.Size || received > MaximumArchiveBytes) throw new InvalidDataException("The release download exceeded its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (Environment.TickCount64 - lastReport > 150)
            {
                progress?.Report(new("downloading_package", received * 100.0 / release.Size, release.Version));
                lastReport = Environment.TickCount64;
            }
        }
        if (received != release.Size) throw new IOException("The release download ended before the expected number of bytes was received.");
    }

    private async Task<HttpResponseMessage> SendTrustedAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            ValidateTrustedUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("DLSSG-Mod-Manager/0.1 (+https://github.com/sdli1995/dlssg_for_sm86)");
            request.Headers.Accept.ParseAdd(uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.github+json" : "application/octet-stream");
            if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            try
            {
                if (response.RequestMessage?.RequestUri is { } finalUri) ValidateTrustedUri(finalUri);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location ?? throw new InvalidDataException("A release redirect had no destination.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    response.Dispose();
                    continue;
                }
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Too many redirects while downloading the release.");
    }

    private static void ValidateTrustedUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || !RedirectHosts.Contains(uri.IdnHost)) throw new InvalidDataException("The release URL is outside the official GitHub download hosts.");
    }

    private static void ValidateReleaseUrls(ReleaseInfo release)
    {
        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var download)
            || !Uri.TryCreate(release.ReleaseUrl, UriKind.Absolute, out var page)) throw new InvalidDataException("Invalid release URL.");
        ValidateTrustedUri(download);
        ValidateTrustedUri(page);
        if (!download.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !download.AbsolutePath.StartsWith("/sdli1995/dlssg_for_sm86/releases/download/", StringComparison.Ordinal)
            || !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !page.AbsolutePath.StartsWith("/sdli1995/dlssg_for_sm86/releases/tag/", StringComparison.Ordinal))
            throw new InvalidDataException("The release URL does not belong to the configured upstream repository.");
        var decodedName = Uri.UnescapeDataString(download.Segments.Last());
        if (!decodedName.Equals(release.AssetName, StringComparison.Ordinal)) throw new InvalidDataException("The release asset URL has an unexpected filename.");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, long limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("The metadata response is too large.");
        await using var source = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        await CopyLimitedAsync(source, output, limit, token).ConfigureAwait(false);
        return output.ToArray();
    }

    private static async Task<long> CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) return copied;
            copied = checked(copied + count);
            if (copied > limit) throw new InvalidDataException("The stream exceeded the accepted size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static bool IsSha256(string? value) => value is not null && Regex.IsMatch(value, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant);
    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');
    private static string SafeError(Exception error) => error.Message.Length <= 500 ? error.Message : error.Message[..500];

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symbolic links and directory junctions are not accepted in the package cache.");
    }

    private static void RejectReparseAncestors(string root, string target)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var current = Path.GetFullPath(target); current is not null && current.Length >= fullRoot.Length; current = Path.GetDirectoryName(current))
            RejectReparsePoint(current);
    }

    private static void EnsureSafeDirectory(string path)
    {
        RejectReparsePoint(path);
        Directory.CreateDirectory(path);
        RejectReparsePoint(path);
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory, CancellationToken token)
    {
        var stack = new Stack<string>();
        stack.Push(directory);
        var count = 0;
        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var current = stack.Pop();
            RejectReparsePoint(current);
            foreach (var item in Directory.EnumerateFileSystemEntries(current))
            {
                token.ThrowIfCancellationRequested();
                if (++count > MaximumEntries * 2) throw new InvalidDataException("The package contains too many filesystem entries.");
                RejectReparsePoint(item);
                if (Directory.Exists(item)) stack.Push(item);
                else yield return item;
            }
        }
    }

    private void TryDeleteStage(string stage)
    {
        try
        {
            var fullPath = Path.GetFullPath(stage);
            if (!string.Equals(Path.GetDirectoryName(fullPath), _cacheRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || !Regex.IsMatch(Path.GetFileName(fullPath), @"^\.staging-[a-f0-9]{32}$", RegexOptions.CultureInvariant)) return;
            if (!Directory.Exists(fullPath)) return;
            DeleteTreeWithoutFollowingLinks(fullPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteTreeWithoutFollowingLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path); return; }
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0) DeleteTreeWithoutFollowingLinks(entry);
            else File.Delete(entry);
        }
        Directory.Delete(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient) _client.Dispose();
        _operationLock.Dispose();
    }

    private sealed record CacheState(ReleaseInfo Release, string PackageKey, string ManifestSha256);
}

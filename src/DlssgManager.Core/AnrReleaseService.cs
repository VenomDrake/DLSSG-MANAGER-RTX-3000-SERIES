using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

/// <summary>Downloads official ANR assets as data. No downloaded script, executable or DLL is run.</summary>
public sealed class AnrReleaseService : IDisposable
{
    public const string RepositoryUrl = "https://github.com/apex-compute/ANR";
    public const string LatestReleaseUrl = "https://api.github.com/repos/apex-compute/ANR/releases/latest";
    public const string ReviewedRelease = "FG-V.B15";
    public const long MaximumArchiveBytes = 128L * 1024 * 1024;
    public const long MaximumExpandedBytes = 512L * 1024 * 1024;
    public const int MaximumEntries = 4096;
    private const int MaximumMetadataBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HashSet<string> RedirectHosts = new(StringComparer.OrdinalIgnoreCase)
    { "github.com", "api.github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "github-releases.githubusercontent.com" };
    private readonly string _cacheRoot;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    public AnrReleaseService(string cacheRoot, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<UpdateResult> EnsureLatestAsync(string variant = "full", IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateVariant(variant);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        ReleasePackage? cached = null;
        try
        {
            EnsureCacheRoot();
            var variantRoot = Path.Combine(_cacheRoot, variant);
            EnsureSafeDirectory(variantRoot);
            progress?.Report(new("checking_update"));
            cached = await LoadCachedAsync(variant, cancellationToken).ConfigureAwait(false);
            ReleaseInfo release;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await SendTrustedAsync(new Uri(LatestReleaseUrl), timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                release = ParseLatestRelease(await ReadLimitedAsync(response.Content, MaximumMetadataBytes, timeout.Token).ConfigureAwait(false), variant);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
            { return new(cached is null ? UpdateState.Failed : UpdateState.OfflineCache, cached, SafeError(error)); }
            if (cached is not null && ReleaseService.SameAsset(cached.Release, release)) return new(UpdateState.UpToDate, cached);
            stage = Path.Combine(variantRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            EnsureSafeDirectory(stage);
            var archive = Path.Combine(stage, "release.zip");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                try { await DownloadAsync(release, archive, progress, timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
                { return new(cached is null ? UpdateState.Failed : UpdateState.OfflineCache, cached, SafeError(error)); }
            }
            if (!(await HashFileAsync(archive, cancellationToken).ConfigureAwait(false)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ANR archive SHA-256 does not match GitHub metadata.");
            progress?.Report(new("verifying_package", null, release.Version));
            var packageDirectory = Path.Combine(stage, "package");
            await ExtractVerifiedArchiveAsync(archive, packageDirectory, cancellationToken).ConfigureAwait(false);
            var package = await ValidatePackageAsync(packageDirectory, release, variant, cancellationToken).ConfigureAwait(false);
            var key = $"{release.ReleaseId}-{release.AssetId}-{release.Sha256[..16].ToLowerInvariant()}";
            var containers = Path.Combine(variantRoot, "packages");
            EnsureSafeDirectory(containers);
            var destination = Path.Combine(containers, key);
            if (Directory.Exists(destination))
            {
                RejectReparsePoint(destination);
                Directory.Move(destination, destination + ".previous-" + Guid.NewGuid().ToString("N"));
            }
            Directory.Move(stage, destination);
            stage = null;
            var state = new CacheState(release, key, variant);
            var temporary = Path.Combine(variantRoot, ".current-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonOptions), cancellationToken).ConfigureAwait(false);
                RejectReparsePoint(Path.Combine(variantRoot, "current.json"));
                File.Move(temporary, Path.Combine(variantRoot, "current.json"), true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            progress?.Report(new("download_complete", 100, release.Version));
            return new(UpdateState.Downloaded, package with { Directory = Path.Combine(destination, "package") });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsDataError(error)) { return new(UpdateState.Failed, cached, SafeError(error)); }
        finally { if (stage is not null) TryDeleteStage(stage); _operationLock.Release(); }
    }

    /// <summary>Rechecks the archive and every extracted file against its contents, including extra files.</summary>
    public async Task<ReleasePackage?> LoadCachedAsync(string variant = "full", CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateVariant(variant);
        try
        {
            var variantRoot = Path.Combine(_cacheRoot, variant);
            var statePath = Path.Combine(variantRoot, "current.json");
            if (!File.Exists(statePath)) return null;
            RejectAllAncestors(statePath);
            if (new FileInfo(statePath).Length > MaximumMetadataBytes) return null;
            var state = JsonSerializer.Deserialize<CacheState>(await File.ReadAllTextAsync(statePath, cancellationToken).ConfigureAwait(false), JsonOptions);
            if (state?.Release is null || state.Variant != variant || string.IsNullOrEmpty(state.PackageKey) || !Regex.IsMatch(state.PackageKey, @"^\d+-\d+-[a-f0-9]{16}$", RegexOptions.CultureInvariant)
                || !IsSha256(state.Release.Sha256) || state.Release.Size is <= 0 or > MaximumArchiveBytes) return null;
            ValidateReleaseUrls(state.Release);
            ValidateAssetIdentity(state.Release, variant);
            if (state.Release.ReleaseId <= 0 || state.Release.AssetId <= 0
                || state.PackageKey != $"{state.Release.ReleaseId}-{state.Release.AssetId}-{state.Release.Sha256[..16].ToLowerInvariant()}") return null;
            var container = Path.Combine(variantRoot, "packages", state.PackageKey);
            var archive = Path.Combine(container, "release.zip");
            RejectAllAncestors(archive);
            if (!File.Exists(archive) || new FileInfo(archive).Length != state.Release.Size
                || !(await HashFileAsync(archive, cancellationToken).ConfigureAwait(false)).Equals(state.Release.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            var directory = Path.Combine(container, "package");
            await VerifyExtractionAgainstArchiveAsync(archive, directory, cancellationToken).ConfigureAwait(false);
            return await ValidatePackageAsync(directory, state.Release, variant, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsDataError(error)) { return null; }
    }

    /// <summary>Validates ANR package metadata and inventories its files. The caller must authenticate the archive first.</summary>
    public async Task<ReleasePackage> ValidatePackageAsync(string directory, ReleaseInfo release, string variant = "full",
        CancellationToken cancellationToken = default)
    {
        ValidateVariant(variant);
        ArgumentNullException.ThrowIfNull(release);
        ValidateReleaseUrls(release);
        ValidateAssetIdentity(release, variant);
        var root = Path.GetFullPath(directory);
        RejectAllAncestors(root);
        var manifest = ResolveSafePackagePath(root, "nr_fg_package.json");
        RejectReparseAncestors(root, manifest);
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 256 * 1024)
            throw new InvalidDataException("ANR package metadata is missing or too large.");
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(manifest, cancellationToken).ConfigureAwait(false));
        var data = document.RootElement;
        if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.EnumerateObject().Count()
            || data.GetProperty("product").GetString() != "ANR FG Beta" || data.GetProperty("kind").GetString() != variant
            || data.GetProperty("version").GetString() != VersionFromTag(release.Version))
            throw new InvalidDataException("ANR package metadata does not match its release or variant.");
        var prefix = variant == "full" ? "game/OptiScaler/custom/Apex Neural Rendering FG/dlssg/" : "Apex Neural Rendering FG/dlssg/";
        var providerHash = data.GetProperty("provider_sha256").GetString();
        var providerPath = ResolveSafePackagePath(root, prefix + "nvngx_dlssg.dll");
        RejectReparseAncestors(root, providerPath);
        if (!IsSha256(providerHash) || !(await HashFileAsync(providerPath, cancellationToken).ConfigureAwait(false))
            .Equals(providerHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The ANR provider does not match package metadata.");
        var files = new List<PackageFile>();
        long total = 0;
        foreach (var path in EnumerateSafeFiles(root, cancellationToken))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            _ = ResolveSafePackagePath(root, relative);
            var size = new FileInfo(path).Length;
            if (size < 0 || (total = checked(total + size)) > MaximumExpandedBytes || files.Count >= MaximumEntries)
                throw new InvalidDataException("The ANR package exceeds the accepted size limits.");
            files.Add(new(relative, size, await HashFileAsync(path, cancellationToken).ConfigureAwait(false)));
        }
        var names = files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var required = new List<string> { prefix + "nvngx_dlssg.dll", prefix + "nr_fg.ini", prefix + "path_table.json", prefix + "size_table.json",
            prefix + "adapters/pack.nrx", prefix + "kernels/pack.nrx", prefix + "kernels-portable/pack.nrx" };
        var programs = data.GetProperty("programs");
        if (programs.ValueKind != JsonValueKind.Array || programs.GetArrayLength() is < 1 or > 64) throw new InvalidDataException("ANR program inventory is invalid.");
        foreach (var program in programs.EnumerateArray())
        {
            var name = program.GetString() ?? "";
            if (!Regex.IsMatch(name, @"^fg-lite-any-[a-z0-9-]+$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Unsafe ANR program name.");
            required.Add(prefix + "programs/" + name + "/pack.nrx");
        }
        if (variant == "full") required.AddRange(["game/dxgi.dll", "game/OptiScaler.ini", "LICENSES/OptiScaler-GPL-3.0.txt"]);
        if (required.Any(p => !names.TryGetValue(p, out var f) || f.Size == 0)) throw new InvalidDataException("A required ANR package file is missing or empty.");
        // A future release is downloaded and inspectable, but never gains installation support from its version alone.
        var supported = release.Version == ReviewedRelease && data.TryGetProperty("code_revision", out var revision)
            && revision.GetString() == "fdfa515491b9140954008df404a79a0c2eda2da1"
            && programs.GetArrayLength() == 12;
        return new(release, root, files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(), supported,
            [new("package_hash", CheckLevel.Pass, release.Sha256), new("package_layout", supported ? CheckLevel.Pass : CheckLevel.Blocked, release.Version)])
            { Provider = ModProvider.Anr, Variant = variant };
    }

    public static ReleaseInfo ParseLatestRelease(ReadOnlyMemory<byte> json, string variant = "full")
    {
        ValidateVariant(variant);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("A draft or prerelease ANR package is not accepted.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = VersionFromTag(tag);
        var expected = "anr-fg-" + version + "-" + variant + ".zip";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == expected).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("A unique complete ANR package was not found.");
        var asset = assets[0];
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !IsSha256(digest[7..])) throw new InvalidDataException("ANR asset SHA-256 metadata is required.");
        var release = new ReleaseInfo(tag, root.GetProperty("id").GetInt64(), asset.GetProperty("id").GetInt64(), expected,
            asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64(), digest[7..].ToLowerInvariant(),
            root.GetProperty("published_at").GetDateTimeOffset(), root.GetProperty("html_url").GetString()!);
        if (release.ReleaseId <= 0 || release.AssetId <= 0 || release.Size is <= 0 or > MaximumArchiveBytes) throw new InvalidDataException("ANR release identity or size is invalid.");
        ValidateReleaseUrls(release);
        ValidateAssetIdentity(release, variant);
        return release;
    }

    private static string VersionFromTag(string tag)
    {
        if (tag is null || !Regex.IsMatch(tag, @"^FG-V\.B[1-9][0-9]{0,3}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Unsupported ANR release tag format.");
        return tag[3..];
    }
    private static void ValidateVariant(string variant) { if (variant is not ("full" or "addon")) throw new ArgumentException("Choose the full or addon ANR package.", nameof(variant)); }
    private static void ValidateAssetIdentity(ReleaseInfo release, string variant)
    {
        if (release.AssetName != "anr-fg-" + VersionFromTag(release.Version) + "-" + variant + ".zip") throw new InvalidDataException("ANR package identity does not match its variant.");
        var expectedUrl = RepositoryUrl + "/releases/download/" + release.Version + "/" + release.AssetName;
        if (release.DownloadUrl != expectedUrl || release.ReleaseUrl != RepositoryUrl + "/releases/tag/" + release.Version)
            throw new InvalidDataException("ANR release URLs do not match their exact release identity.");
    }
    private static bool IsDataError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException
        or JsonException or CryptographicException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException;
    private static bool IsSha256(string? value) => value is not null && Regex.IsMatch(value, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant);
    private static string SafeError(Exception error) => error.Message.Length <= 500 ? error.Message : error.Message[..500];
    private void EnsureCacheRoot() { RejectAllAncestors(_cacheRoot); EnsureSafeDirectory(_cacheRoot); }
    private static void RejectAllAncestors(string path)
    { for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current)) RejectReparsePoint(current); }

    private static async Task VerifyExtractionAgainstArchiveAsync(string archivePath, string directory, CancellationToken token)
    {
        RejectAllAncestors(directory);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is < 1 or > MaximumEntries) throw new InvalidDataException("Invalid ANR archive entry count.");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var relative = (isDirectory ? entry.FullName[..^1] : entry.FullName).Replace('\\', '/');
            var target = ResolveSafePackagePath(directory, relative);
            RejectReparseAncestors(directory, target);
            if (isDirectory) { if (!Directory.Exists(target)) throw new InvalidDataException("An extracted ANR directory is missing."); continue; }
            if (!expected.Add(relative) || (size = checked(size + entry.Length)) > MaximumExpandedBytes || !File.Exists(target) || new FileInfo(target).Length != entry.Length)
                throw new InvalidDataException("ANR extracted inventory differs from its verified archive.");
            await using var source = entry.Open();
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token).ConfigureAwait(false)).ToLowerInvariant();
            if (!(await HashFileAsync(target, token).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An extracted ANR file differs from its verified archive.");
        }
        if (EnumerateSafeFiles(directory, token).Any(p => !expected.Contains(Path.GetRelativePath(directory, p).Replace('\\', '/'))))
            throw new InvalidDataException("An unlisted file is present in the ANR package cache.");
    }

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
            request.Headers.UserAgent.ParseAdd("DLSSG-Mod-Manager-ANR/0.4 (+https://github.com/apex-compute/ANR)");
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
            || !download.AbsolutePath.StartsWith("/apex-compute/ANR/releases/download/", StringComparison.Ordinal)
            || !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !page.AbsolutePath.StartsWith("/apex-compute/ANR/releases/tag/", StringComparison.Ordinal))
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
            if (!(string.Equals(Path.GetDirectoryName(fullPath), Path.Combine(_cacheRoot, "full"), StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetDirectoryName(fullPath), Path.Combine(_cacheRoot, "addon"), StringComparison.OrdinalIgnoreCase))
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
    private sealed record CacheState(ReleaseInfo Release, string PackageKey, string Variant);
}

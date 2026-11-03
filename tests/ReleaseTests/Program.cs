using DlssgManager.Core;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;

// Offline adversarial and lifecycle tests. Optional --live additionally downloads
// and verifies the official GitHub release; no game directory is ever touched.
var testRoot = Path.Combine(AppContext.BaseDirectory, "testdata-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var tests = 0;
void Require(bool value, string label)
{
    if (!value) throw new Exception(label);
    Console.WriteLine("PASS " + label);
    tests++;
}

foreach (var bad in new[] { "../outside", "folder/../outside", "C:/outside", "file:stream", "CON.dll", "folder./file", "foo\\..\\bar", "/rooted", "foo//bar" })
{
    var rejected = false;
    try { ReleaseService.ResolveSafePackagePath(testRoot, bad); }
    catch (InvalidDataException) { rejected = true; }
    Require(rejected, "reject unsafe path " + bad);
}

var fixture = BuildArchive();
using var client = new HttpClient(new FakeHandler(fixture));
using var updater = new ReleaseService(Path.Combine(testRoot, "cache"), client);
var first = await updater.EnsureLatestAsync();
Require(first.State == UpdateState.Downloaded && first.Package?.SupportedLayout == true, "verified initial download");
var second = await updater.EnsureLatestAsync();
Require(second.State == UpdateState.UpToDate, "same release identity yields up-to-date");
Require(second.Package!.Files.Count == 10, "manifest files verified");

using var offlineClient = new HttpClient(new OfflineHandler());
using var offline = new ReleaseService(Path.Combine(testRoot, "cache"), offlineClient);
Require((await offline.EnsureLatestAsync()).State == UpdateState.OfflineCache, "offline cache is not called latest");
await File.WriteAllTextAsync(Path.Combine(second.Package.Directory, "version.dll"), "tampered");
Require(await updater.LoadCachedAsync() is null, "tampered file invalidates cache");
Require((await updater.EnsureLatestAsync()).State == UpdateState.Downloaded, "tampered cache recovered with fresh verified package");

var latest = (await updater.LoadCachedAsync())!;
var changed = latest.Release with { AssetId = latest.Release.AssetId + 1 };
Require(!ReleaseService.SameAsset(latest.Release, changed), "asset replacement detected with same version");
Require(!ReleaseService.SameAsset(latest.Release, latest.Release with { ReleaseId = latest.Release.ReleaseId + 1 }), "release replacement detected with same version");
Require(!ReleaseService.SameAsset(latest.Release, latest.Release with { Sha256 = new string('a', 64) }), "digest change detected with same release and asset IDs");

var evilZip = Path.Combine(testRoot, "evil.zip");
using (var zip = ZipFile.Open(evilZip, ZipArchiveMode.Create))
{
    using var writer = new StreamWriter(zip.CreateEntry("../escape.dll").Open());
    writer.Write("evil");
}
var evilRejected = false;
try { await ReleaseService.ExtractVerifiedArchiveAsync(evilZip, Path.Combine(testRoot, "evil")); }
catch (InvalidDataException) { evilRejected = true; }
Require(evilRejected && !Directory.Exists(Path.Combine(testRoot, "evil")), "traversal rejected before extraction starts");

var duplicateZip = Path.Combine(testRoot, "duplicate.zip");
using (var zip = ZipFile.Open(duplicateZip, ZipArchiveMode.Create))
{
    zip.CreateEntry("Version.dll");
    zip.CreateEntry("version.dll");
}
var duplicateRejected = false;
try { await ReleaseService.ExtractVerifiedArchiveAsync(duplicateZip, Path.Combine(testRoot, "duplicate")); }
catch (InvalidDataException) { duplicateRejected = true; }
Require(duplicateRejected, "case duplicate rejected");

using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    var stopped = false;
    try { await updater.EnsureLatestAsync(cancellationToken: canceled.Token); }
    catch (OperationCanceledException) { stopped = true; }
    Require(stopped, "cancellation is not treated as offline");
}

// A verified new download must remain available for inspection while installation
// stays blocked until the manager has an explicit compatibility recipe.
using (var futureClient = new HttpClient(new FakeHandler(BuildArchive("0.4.2"), "0.4.2")))
using (var futureUpdater = new ReleaseService(Path.Combine(testRoot, "future"), futureClient))
{
    var future = await futureUpdater.EnsureLatestAsync();
    Require(future.State == UpdateState.Downloaded && future.Package is { SupportedLayout: false }
        && future.Package.Checks.Any(c => c.Code == "package_layout" && c.Level == CheckLevel.Blocked),
        "verified future 0.4.2 downloads but cannot be installed");
    Require((await futureUpdater.LoadCachedAsync()) is { SupportedLayout: false }, "unsupported future package remains verifiable in cache");
}
using (var changedModelClient = new HttpClient(new FakeHandler(BuildArchive(runtimeModel: "310.10.0"))))
using (var changedModelUpdater = new ReleaseService(Path.Combine(testRoot, "changed-model"), changedModelClient))
{
    Require((await changedModelUpdater.EnsureLatestAsync()).Package is { SupportedLayout: false },
        "changed runtime model under same version has no compatibility recipe");
}
using (var changedVersionClient = new HttpClient(new FakeHandler(BuildArchive(runtimeVersion: "310.10.0.0"))))
using (var changedVersionUpdater = new ReleaseService(Path.Combine(testRoot, "changed-runtime"), changedVersionClient))
{
    Require((await changedVersionUpdater.EnsureLatestAsync()).Package is { SupportedLayout: false },
        "changed runtime version under same release has no compatibility recipe");
}

if (args.Contains("--live"))
{
    using var live = new ReleaseService(Path.Combine(testRoot, "live"));
    var liveFirst = await live.EnsureLatestAsync(new Progress<OperationProgress>(p =>
    {
        if (p.Stage != "downloading_package") Console.WriteLine(p.Stage + " " + p.Detail);
    }));
    Require(liveFirst.State == UpdateState.Downloaded && liveFirst.Package is not null, "live official release verifies");
    Require((await live.EnsureLatestAsync()).State == UpdateState.UpToDate, "live second run is up-to-date");
}
Console.WriteLine($"{tests} release checks passed.");

static byte[] BuildArchive(string version = "0.4.1", string runtimeModel = "310.9.16", string runtimeVersion = "310.9.16.0")
{
    var paths = new[]
    {
        "version.dll", "dlssg_sm86.ini", "dlssg_sm86/backend/sm86_backend.dll", "dlssg_sm86/kernels/fg_kernels.dll",
        "dlssg_sm86/runtime/nvngx_dlssg.dll", "dlssg_sm86/sr/nvngx_dlss.dll", "dlssg_sm86/sr/sr_kernels.dll",
        "dlssg_sm86/xefg/xefg_kernels.dll", "LICENSE.md", "THIRD_PARTY_NOTICES.txt"
    };
    var files = paths.Select(p => new { path = p, data = Encoding.UTF8.GetBytes("fixture-" + p) }).ToArray();
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
    {
        foreach (var file in files)
        {
            using var output = zip.CreateEntry(file.path).Open();
            output.Write(file.data);
        }
        using var manifest = zip.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(manifest, new
        {
            project = "dlssg", bundle = new { version, runtime_model = runtimeModel, runtime_version = runtimeVersion },
            files = files.Select(f => new { f.path, size = f.data.Length, sha256 = Convert.ToHexString(SHA256.HashData(f.data)) })
        });
    }
    return stream.ToArray();
}

sealed class FakeHandler(byte[] fixture, string version = "0.4.1") : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        if (request.RequestUri!.AbsoluteUri == ReleaseService.LatestReleaseUrl)
            response.Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = 10, draft = false, prerelease = false, tag_name = version, published_at = "2026-10-27T06:27:00Z",
                html_url = ReleaseService.RepositoryUrl + "/releases/tag/" + version,
                assets = new[]
                {
                    new
                    {
                        id = 20, name = "dlssg-release-" + version + "-x64.zip", size = fixture.Length,
                        digest = "sha256:" + Convert.ToHexString(SHA256.HashData(fixture)),
                        browser_download_url = ReleaseService.RepositoryUrl + "/releases/download/" + version + "/dlssg-release-" + version + "-x64.zip"
                    }
                }
            }));
        else response.Content = new ByteArrayContent(fixture);
        return Task.FromResult(response);
    }
}

sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        throw new HttpRequestException("offline fixture");
}

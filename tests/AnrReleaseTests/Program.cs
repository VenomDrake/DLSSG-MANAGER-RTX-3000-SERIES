using DlssgManager.Core;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Packages are inert synthetic bytes; tests never load a DLL or touch a game.
var root = Path.Combine(AppContext.BaseDirectory, "testdata-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int count = 0;
void Require(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
async Task Reject(Func<Task> action, string name)
{
    bool rejected = false;
    try { await action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException) { rejected = true; }
    Require(rejected, name);
}

byte[] full = Fixture.Archive("full");
byte[] addon = Fixture.Archive("addon");
var fake = new PackageHandler(full, addon);
using var client = new HttpClient(fake);
using var service = new AnrReleaseService(Path.Combine(root, "cache"), client);
var first = await service.EnsureLatestAsync();
Require(first.State == UpdateState.Downloaded && first.Package is { Provider: ModProvider.Anr, Variant: "full", SupportedLayout: true }, "verified full package download and recipe");
Require((await service.EnsureLatestAsync()).State == UpdateState.UpToDate, "same identity uses verified full cache");
var add = await service.EnsureLatestAsync("addon");
Require(add.State == UpdateState.Downloaded && add.Package is { Provider: ModProvider.Anr, Variant: "addon", SupportedLayout: true }, "addon downloaded to a separate cache");
Require(first.Package!.Release.AssetId != add.Package!.Release.AssetId && first.Package.Directory != add.Package.Directory, "variant identities and directories differ");
Require((await service.LoadCachedAsync("full"))?.Variant == "full" && (await service.LoadCachedAsync("addon"))?.Variant == "addon", "both variant caches reverify independently");
Require(first.Package.Files.Any(f => f.Path.EndsWith("kernels/pack.nrx")) && first.Package.Files.All(f => !string.IsNullOrWhiteSpace(f.Sha256)), "payload inventory includes model packs with file digests");
using var offlineClient = new HttpClient(new OfflineHandler());
using var offline = new AnrReleaseService(Path.Combine(root, "cache"), offlineClient);
Require((await offline.EnsureLatestAsync()).State == UpdateState.OfflineCache, "offline verified cache is distinct from latest");
await File.WriteAllTextAsync(Path.Combine(first.Package.Directory, "game", "OptiScaler.ini"), "tampered");
Require(await service.LoadCachedAsync() is null, "modified cached INI invalidates full cache");
Require((await service.LoadCachedAsync("addon")) is not null, "tampered full cache does not affect addon cache");
Require((await service.EnsureLatestAsync()).State == UpdateState.Downloaded, "modified cache is recovered by a fresh verified download");
var clean = (await service.LoadCachedAsync())!;
await File.WriteAllTextAsync(Path.Combine(clean.Directory, "extra.dll"), "unknown");
Require(await service.LoadCachedAsync() is null, "extra cached file invalidates inventory");
Require((await service.EnsureLatestAsync()).State == UpdateState.Downloaded, "cache with extra file is isolated and replaced");
clean = (await service.LoadCachedAsync())!;
var archive = Path.Combine(Path.GetDirectoryName(clean.Directory)!, "release.zip");
await File.AppendAllTextAsync(archive, "tamper");
Require(await service.LoadCachedAsync() is null, "tampered cached archive is rejected");

fake.AssetGeneration++;
Require((await service.EnsureLatestAsync()).State == UpdateState.Downloaded, "asset replacement identity is detected with unchanged tag");
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    bool canceled = false;
    try { await service.EnsureLatestAsync(cancellationToken: cts.Token); } catch (OperationCanceledException) { canceled = true; }
    Require(canceled, "cancellation remains cancellation");
}
await Reject(() => service.EnsureLatestAsync("../outside"), "variant cannot escape cache");

foreach (var mutation in new[] { "missing-digest", "external-url", "foreign-repo", "wrong-tag-url", "duplicate-asset", "draft", "prerelease", "oversize" })
{
    var bytes = Fixture.Metadata(full, addon, mutation: mutation);
    await Reject(() => Task.Run(() => AnrReleaseService.ParseLatestRelease(bytes)), "metadata rejects " + mutation);
}
using (var futureClient = new HttpClient(new PackageHandler(Fixture.Archive("full", "V.B16"), Fixture.Archive("addon", "V.B16"), "V.B16")))
using (var future = new AnrReleaseService(Path.Combine(root, "future"), futureClient))
{
    var result = await future.EnsureLatestAsync();
    Require(result.State == UpdateState.Downloaded && result.Package is { SupportedLayout: false }, "future official package downloads without automatic recipe support");
    Require((await future.LoadCachedAsync()) is { SupportedLayout: false }, "future package cache remains inspectable and blocked");
}
foreach (var bad in new[] { "../outside", "folder/../outside", "C:/outside", "x:stream", "CON.dll", "folder./x", "foo//bar" })
    await Reject(() => Task.Run(() => AnrReleaseService.ResolveSafePackagePath(root, bad)), "unsafe path rejected: " + bad);
foreach (var kind in new[] { "traversal", "duplicate", "symlink", "file-parent" })
{
    var zip = Path.Combine(root, kind + ".zip");
    using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        if (kind == "traversal") z.CreateEntry("../outside.dll");
        if (kind == "duplicate") { z.CreateEntry("A.dll"); z.CreateEntry("a.dll"); }
        if (kind == "symlink") z.CreateEntry("link").ExternalAttributes = unchecked((int)0xA1FF0000);
        if (kind == "file-parent") { z.CreateEntry("file"); z.CreateEntry("file/x"); }
    }
    var dest = Path.Combine(root, "extract-" + kind);
    await Reject(() => AnrReleaseService.ExtractVerifiedArchiveAsync(zip, dest), "archive rejected before extraction: " + kind);
    Require(!Directory.Exists(dest), "rejected archive leaves destination absent: " + kind);
}
foreach (var problem in new[] { "provider-hash", "missing-pack", "wrong-kind", "wrong-version", "duplicate-metadata" })
{
    using var badClient = new HttpClient(new PackageHandler(Fixture.Archive("full", problem: problem), addon));
    using var invalid = new AnrReleaseService(Path.Combine(root, "invalid-" + problem), badClient);
    Require((await invalid.EnsureLatestAsync()).State == UpdateState.Failed, "package rejects " + problem);
    Require(await invalid.LoadCachedAsync() is null, "invalid package never replaces cache: " + problem);
}
using (var redirectClient = new HttpClient(new RedirectHandler()))
using (var redirect = new AnrReleaseService(Path.Combine(root, "redirect"), redirectClient))
    Require((await redirect.EnsureLatestAsync()).State == UpdateState.Failed, "redirect to untrusted HTTP host rejected");
if (args.Contains("--live"))
{
    using var live = new AnrReleaseService(Path.Combine(root, "live"));
    foreach (var variant in new[] { "full", "addon" })
    {
        var result = await live.EnsureLatestAsync(variant);
        Require(result.State == UpdateState.Downloaded && result.Package is { SupportedLayout: true }, "live official " + variant + " archive and provider hash verify");
        Require((await live.EnsureLatestAsync(variant)).State == UpdateState.UpToDate, "live " + variant + " second check is current");
    }
}
Console.WriteLine($"ANR release service: {count} checks passed; no downloaded code was executed.");

static class Fixture
{
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public static byte[] Archive(string variant, string version = "V.B15", string problem = "")
    {
        var prefix = variant == "full" ? "game/OptiScaler/custom/Apex Neural Rendering FG/dlssg/" : "Apex Neural Rendering FG/dlssg/";
        var files = new Dictionary<string, byte[]>();
        foreach (var suffix in new[] { "nvngx_dlssg.dll", "nr_fg.ini", "path_table.json", "size_table.json", "adapters/pack.nrx", "kernels/pack.nrx", "kernels-portable/pack.nrx" })
            files[prefix + suffix] = Encoding.UTF8.GetBytes("inert-" + suffix);
        var programs = new[] { "coopmat", "dp4a", "packed", "portable-linalg", "portable", "portable-xmx" }.SelectMany(p => new[] { "fg-lite-any-" + p + "-pq10", "fg-lite-any-" + p + "-srgb8" }).ToArray();
        foreach (var program in programs) files[prefix + "programs/" + program + "/pack.nrx"] = Encoding.UTF8.GetBytes(program);
        if (variant == "full")
            foreach (var p in new[] { "game/dxgi.dll", "game/OptiScaler.ini", "LICENSES/OptiScaler-GPL-3.0.txt" }) files[p] = Encoding.UTF8.GetBytes("inert-" + p);
        files["nr_fg_package.json"] = JsonSerializer.SerializeToUtf8Bytes(new
        {
            product = "ANR FG Beta", version = problem == "wrong-version" ? "V.B99" : version,
            kind = problem == "wrong-kind" ? "other" : variant, code_revision = "fdfa515491b9140954008df404a79a0c2eda2da1", programs,
            provider_sha256 = problem == "provider-hash" ? new string('a', 64) : Hash(files[prefix + "nvngx_dlssg.dll"])
        });
        if (problem == "duplicate-metadata") files["nr_fg_package.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["nr_fg_package.json"]).Replace("{", "{\"kind\":\"full\",", StringComparison.Ordinal));
        if (problem == "missing-pack") files.Remove(prefix + "kernels/pack.nrx");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var s = zip.CreateEntry(file.Key).Open(); s.Write(file.Value); }
        return stream.ToArray();
    }
    public static byte[] Metadata(byte[] full, byte[] addon, string version = "V.B15", string mutation = "", int generation = 0)
    {
        string tag = "FG-" + version;
        var assets = new[] { ("full", full), ("addon", addon) }.Select((pair, i) => new
        {
            id = 20 + i + generation * 10,
            name = "anr-fg-" + version + "-" + pair.Item1 + ".zip",
            size = mutation == "oversize" ? AnrReleaseService.MaximumArchiveBytes + 1 : pair.Item2.Length,
            digest = mutation == "missing-digest" ? "" : "sha256:" + Hash(pair.Item2),
            browser_download_url = (mutation == "external-url" ? "https://evil.example" : mutation == "foreign-repo" ? "https://github.com/foreign/ANR" : AnrReleaseService.RepositoryUrl)
                + "/releases/download/" + (mutation == "wrong-tag-url" ? "FG-V.B1" : tag) + "/anr-fg-" + version + "-" + pair.Item1 + ".zip"
        }).ToList();
        if (mutation == "duplicate-asset") assets.Add(assets[0]);
        return JsonSerializer.SerializeToUtf8Bytes(new { id = 10, draft = mutation == "draft", prerelease = mutation == "prerelease", tag_name = tag, published_at = "2026-11-12T17:49:28Z", html_url = AnrReleaseService.RepositoryUrl + "/releases/tag/" + tag, assets });
    }
}
sealed class PackageHandler(byte[] full, byte[] addon, string version = "V.B15") : HttpMessageHandler
{
    public int AssetGeneration { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        result.Content = new ByteArrayContent(request.RequestUri!.AbsoluteUri == AnrReleaseService.LatestReleaseUrl ? Fixture.Metadata(full, addon, version, generation: AssetGeneration)
            : request.RequestUri.AbsolutePath.EndsWith("-addon.zip") ? addon : full);
        return Task.FromResult(result);
    }
}
sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new HttpRequestException("offline fixture");
}
sealed class RedirectHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { var result = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request }; result.Headers.Location = new Uri("http://evil.example/archive.zip"); return Task.FromResult(result); }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DlssgManager.Core;
using Microsoft.Win32.SafeHandles;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("These tests inspect Windows PE fixtures and file replacement semantics.");
var count = 0;
await Test("release metadata requires a newer stable version and both verified official assets", () =>
{
    var release = Release();
    var info = ManagerUpdateService.ParseRelease(Bytes(release), "v0.2.0+local");
    Assert(info?.Version == "0.3.0", "New release not found.");
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.3.0.0") is null, "Same version offered.");
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.4.0") is null, "Downgrade offered.");
    release["prerelease"] = true;
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Prerelease offered.");
    release = Release(); release["tag_name"] = "v0.3.0-rc1";
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Unstable tag offered.");
    release = Release(); release["assets"]![0]!["digest"] = null;
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Missing SHA allowed.");
    release = Release(); release["assets"]![0]!["browser_download_url"] = "https://example.com/update.zip";
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Unofficial asset allowed.");
    release = Release(); release["assets"]!.AsArray().RemoveAt(1);
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Partial release allowed.");
    release = Release(); release["assets"]!.AsArray().Add(release["assets"]![0]!.DeepClone());
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Duplicate asset allowed.");
    release = Release(); release["html_url"] = "https://github.com:8443/" + ManagerUpdateService.Repository + "/releases/tag/v0.3.0";
    Assert(ManagerUpdateService.ParseRelease(Bytes(release), "0.2.0") is null, "Unexpected HTTPS port allowed.");
    return Task.CompletedTask;
});
await Test("background check is quiet on offline, malformed, missing, huge or untrusted metadata", async () =>
{
    using var temp = new Temp();
    foreach (var body in new[] { "{}", "not json", "null", "{\"draft\":false}", new string('x', 2 * 1024 * 1024 + 1) })
    {
        using var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response(body))));
        using var service = new ManagerUpdateService(temp.Root, client);
        Assert(await service.CheckAsync("0.2.0") is null, "Malformed metadata escaped quiet check.");
    }
    using var offline = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("offline fixture")));
    using var unavailable = new ManagerUpdateService(temp.Root, offline);
    Assert(await unavailable.CheckAsync("0.2.0") is null, "Offline check did not stay quiet.");
    var requests = 0;
    using var redirect = new HttpClient(new FakeHandler((_, _) =>
    {
        requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com/payload") } });
    }));
    using var blocked = new ManagerUpdateService(temp.Root, redirect);
    Assert(await blocked.CheckAsync("0.2.0") is null && requests == 1, "Untrusted redirect was requested.");
});
await Test("background check uses official endpoint, honors cancellation and accepts a valid update", async () =>
{
    using var temp = new Temp();
    using var client = new HttpClient(new FakeHandler((request, _) =>
    {
        Assert(request.RequestUri?.AbsoluteUri == ManagerUpdateService.LatestReleaseUrl, "Wrong repository endpoint.");
        return Task.FromResult(Response(Release().ToJsonString()));
    }));
    using var service = new ManagerUpdateService(temp.Root, client);
    Assert((await service.CheckAsync("0.2.0"))?.Version == "0.3.0", "Valid update missing.");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Throws<OperationCanceledException>(() => service.CheckAsync("0.2.0", cancelled.Token));
});
await Test("portable ZIP extracts only the complete root inventory", async () =>
{
    using var temp = new Temp();
    var archive = temp.Path("safe.zip");
    Zip(archive, ManagerUpdateService.OwnedFiles.Select(n => (n, "new:" + n, 0)));
    var files = await ManagerUpdateService.ExtractPortableAsync(archive, temp.Path("safe"), default);
    Assert(files.Count == 7 && files.All(f => ManagerUpdateService.IsHash(f.Sha256)), "Inventory incomplete.");
    var invalid = new[]
    {
        ManagerUpdateService.OwnedFiles.Take(6).Select(n => (n, "data", 0)),
        ManagerUpdateService.OwnedFiles.Select(n => (n == "LICENSE" ? "../LICENSE" : n, "data", 0)),
        ManagerUpdateService.OwnedFiles.Select(n => (n == "LICENSE" ? "README.md" : n, "data", 0)),
        ManagerUpdateService.OwnedFiles.Select(n => (n, "data", n == "LICENSE" ? unchecked((int)0xA1FF0000) : 0)),
        ManagerUpdateService.OwnedFiles.Select(n => (n, "data", n == "LICENSE" ? 0x400 : 0)),
        ManagerUpdateService.OwnedFiles.Select(n => (n, "data", 0)).Append(("foreign.dll", "data", 0))
    };
    var i = 0;
    foreach (var entries in invalid)
    {
        var bad = temp.Path("bad-" + i + ".zip"); Zip(bad, entries);
        await Throws<InvalidDataException>(() => ManagerUpdateService.ExtractPortableAsync(bad, temp.Path("bad-" + i++), default));
    }
    Assert(!File.Exists(temp.Path("LICENSE")), "Traversal wrote outside extraction root.");
});
await Test("PE fixtures identify x64 product and version without execution", () =>
{
    Assert(ManagerUpdateService.ReadManagerVersion(Fixture("Old.exe")) == new Version(0, 2, 0), "Old PE version incorrect.");
    Assert(ManagerUpdateService.ReadManagerVersion(Fixture("New.exe")) == new Version(0, 3, 0), "New PE version incorrect.");
    return Task.CompletedTask;
});
await Test("plans bind paths and hashes, rejecting tampering and unknown restart arguments", async () =>
{
    using var fixture = await UpdateFixture.Create();
    var plan = fixture.Plan;
    var loaded = await ManagerUpdateWorker.ReadPlanAsync(plan.PlanPath, plan.PlanSha256, false, default);
    Assert(loaded.TargetExePath == plan.TargetExePath, "Plan target changed.");
    await File.AppendAllTextAsync(plan.PlanPath, " ");
    await Throws<InvalidDataException>(() => ManagerUpdateWorker.ReadPlanAsync(plan.PlanPath, plan.PlanSha256, false, default));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateWorker.ValidatePlanStructure(plan with { ResultPath = fixture.Path("escape.json") }, plan.PlanPath));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateWorker.ValidatePlanStructure(plan with { Files = plan.Files.Skip(1).ToArray() }, plan.PlanPath));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateWorker.ValidatePlanStructure(plan with { ToVersion = "0.1.0" }, plan.PlanPath));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateService.ValidateLaunchArguments(["--apply-manager-update", "evil"], plan.DataDirectory));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateService.ValidateLaunchArguments(["--data-dir", fixture.Root], plan.DataDirectory));
    ManagerUpdateService.ValidateLaunchArguments(["--game", "a folder with spaces", "--data-dir", plan.DataDirectory], plan.DataDirectory);
    await File.WriteAllTextAsync(plan.PlanPath, new string('x', 65537));
    await Throws<InvalidDataException>(() => ManagerUpdateWorker.ReadPlanAsync(plan.PlanPath, plan.PlanSha256, false, default));
});
await Test("payload validation detects modified files and rejects mismatching executable version", async () =>
{
    using var fixture = await UpdateFixture.Create();
    await ManagerUpdateWorker.ValidatePayloadAsync(fixture.Plan, default);
    var candidate = System.IO.Path.Combine(fixture.Plan.CandidateDirectory, "LICENSE");
    await File.AppendAllTextAsync(candidate, "tampered");
    await Throws<InvalidDataException>(() => ManagerUpdateWorker.ValidatePayloadAsync(fixture.Plan, default));
    using var wrong = await UpdateFixture.Create();
    await Throws<InvalidDataException>(() => ManagerUpdateWorker.ValidatePayloadAsync(wrong.Plan with { ToVersion = "0.4.0" }, default));
});
await Test("portable update replaces owned files atomically, executable last, leaving unrelated files intact", async () =>
{
    using var fixture = await UpdateFixture.Create();
    var plan = fixture.Plan;
    var targetDir = System.IO.Path.GetDirectoryName(plan.TargetExePath)!;
    await ManagerUpdateWorker.ApplyPortableAsync(plan, default, applied =>
    {
        if (applied < plan.Files.Count)
            Assert(ManagerUpdateService.ReadManagerVersion(plan.TargetExePath) == new Version(0, 2, 0), "Executable was replaced before all sidecars.");
    });
    foreach (var file in plan.Files)
    {
        Assert(await ManagerUpdateService.HashAsync(System.IO.Path.Combine(targetDir, file.Name), default) == file.Sha256, "Wrong installed bytes.");
        Assert(await ManagerUpdateService.HashAsync(System.IO.Path.Combine(plan.OperationDirectory, "backup", file.Name), default) == fixture.OriginalHashes[file.Name], "Wrong backup bytes.");
    }
    Assert(await File.ReadAllTextAsync(fixture.Path("target", "unrelated.txt")) == "keep", "Unrelated file changed.");
    Assert(!Directory.EnumerateDirectories(targetDir, ".manager-update-*").Any(), "Staging directory leaked.");
});
await Test("portable failures roll back all originals, including an already replaced executable", async () =>
{
    foreach (var failAt in new[] { 1, 4, 7 })
    {
        using var fixture = await UpdateFixture.Create();
        await Throws<IOException>(() => ManagerUpdateWorker.ApplyPortableAsync(fixture.Plan, default, count =>
        { if (count == failAt) throw new IOException("injected fixture failure"); }));
        foreach (var original in fixture.OriginalHashes)
            Assert(await ManagerUpdateService.HashAsync(fixture.Path("target", original.Key), default) == original.Value, "Rollback did not recover " + original.Key);
        Assert(await File.ReadAllTextAsync(fixture.Path("target", "unrelated.txt")) == "keep", "Rollback changed unrelated data.");
    }
});
await Test("rollback removes newly added sidecars and cancellation leaves target unchanged", async () =>
{
    using var fixture = await UpdateFixture.Create();
    File.Delete(fixture.Path("target", "LICENSE"));
    await Throws<IOException>(() => ManagerUpdateWorker.ApplyPortableAsync(fixture.Plan, default, _ => throw new IOException("injected")));
    Assert(!File.Exists(fixture.Path("target", "LICENSE")), "Rollback retained newly added file.");
    Assert(ManagerUpdateService.ReadManagerVersion(fixture.Plan.TargetExePath) == new Version(0, 2, 0), "Rollback changed executable.");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Throws<OperationCanceledException>(() => ManagerUpdateWorker.ApplyPortableAsync(fixture.Plan, cancelled.Token));
    Assert(ManagerUpdateService.ReadManagerVersion(fixture.Plan.TargetExePath) == new Version(0, 2, 0), "Cancellation mutated executable.");
});
await Test("locked executable causes a safe rollback and no process is terminated", async () =>
{
    using var fixture = await UpdateFixture.Create();
    using (var locked = new FileStream(fixture.Plan.TargetExePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await Throws<IOException>(() => ManagerUpdateWorker.ApplyPortableAsync(fixture.Plan, default));
    foreach (var original in fixture.OriginalHashes)
        Assert(await ManagerUpdateService.HashAsync(fixture.Path("target", original.Key), default) == original.Value, "Locked target was not preserved.");
});
await Test("MSI pending-reboot status accepts only expected old or new versions", async () =>
{
    using var fixture = await UpdateFixture.Create();
    var installed = fixture.Plan with { Mode = ManagerUpdateMode.Installed };
    ManagerUpdateWorker.VerifyInstalledVersion(installed, true);
    ThrowsSync<InvalidDataException>(() => ManagerUpdateWorker.VerifyInstalledVersion(installed, false));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateWorker.VerifyInstalledVersion(installed with { FromVersion = "0.1.0" }, true));
    ThrowsSync<InvalidDataException>(() => ManagerUpdateWorker.VerifyInstalledVersion(fixture.Plan, true));
    var fakeMsi = fixture.Path("fake.msi"); await File.WriteAllTextAsync(fakeMsi, "not an installer");
    ThrowsSync<InvalidDataException>(() => ManagerUpdateMsi.Validate(fakeMsi, "0.3.0"));
});
await Test("results can only be read from the manager update cache", async () =>
{
    using var fixture = await UpdateFixture.Create();
    var result = new ManagerUpdateResult(true, false, "manager_update_completed", "", "0.2.0", "0.3.0");
    await File.WriteAllBytesAsync(fixture.Plan.ResultPath, JsonSerializer.SerializeToUtf8Bytes(result));
    Assert(ManagerUpdateWorker.ReadResult(fixture.Plan.ResultPath, fixture.Plan.DataDirectory)?.Success == true, "Result missing.");
    var foreign = fixture.Path("update-result.json"); File.Copy(fixture.Plan.ResultPath, foreign);
    Assert(ManagerUpdateWorker.ReadResult(foreign, fixture.Plan.DataDirectory) is null, "Foreign result accepted.");
    await File.WriteAllTextAsync(fixture.Plan.ResultPath, new string('x', 16385));
    Assert(ManagerUpdateWorker.ReadResult(fixture.Plan.ResultPath, fixture.Plan.DataDirectory) is null, "Huge result accepted.");
});
await Test("worker rejects an invalid plan without executing or relaunching any target", async () =>
{
    using var fixture = await UpdateFixture.Create();
    var result = await ManagerUpdateWorker.RunAsync(fixture.Plan.PlanPath, new string('0', 64));
    Assert(!result.Success && result.Code == "manager_update_failed", "Invalid worker plan did not fail safely.");
    Assert(ManagerUpdateService.ReadManagerVersion(fixture.Plan.TargetExePath) == new Version(0, 2, 0), "Rejected plan mutated target.");
    Assert(!File.Exists(fixture.Plan.ResultPath), "Untrusted plan was used to write a result.");
});
await Test("cache pruning retains newest, failed, incomplete, future, active and unexpected-content operations", async () =>
{
    using var temp = new Temp();
    var data = temp.Path("data"); var cache = System.IO.Path.Combine(data, "manager-updates");
    Directory.CreateDirectory(cache);
    async Task<string> Completed(int age, bool success = true, string version = "0.3.0")
    {
        var directory = System.IO.Path.Combine(cache, "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(System.IO.Path.Combine(directory, "candidate"));
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "candidate", "LICENSE"), "fixture");
        var resultPath = System.IO.Path.Combine(directory, "update-result.json");
        var result = new ManagerUpdateResult(success, false, success ? "manager_update_completed" : "manager_update_failed", "", "0.2.0", version);
        await File.WriteAllBytesAsync(resultPath, JsonSerializer.SerializeToUtf8Bytes(result));
        File.SetLastWriteTimeUtc(resultPath, DateTime.UtcNow.AddDays(-age));
        return directory;
    }
    var old = await Completed(10); var newest = await Completed(1); var failed = await Completed(20, false);
    var future = await Completed(30, true, "0.4.0"); var active = await Completed(40); var foreign = await Completed(50);
    var incomplete = System.IO.Path.Combine(cache, "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(incomplete);
    var unknown = System.IO.Path.Combine(cache, "unrelated-folder"); Directory.CreateDirectory(unknown);
    await File.WriteAllTextAsync(System.IO.Path.Combine(foreign, "my-notes.txt"), "preserve");
    var activityPath = System.IO.Path.Combine(active, ManagerUpdateCleanup.ActivityFileName);
    using var service = new ManagerUpdateService(cache);
    using (var activity = new FileStream(activityPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    {
        service.PruneCompletedUpdates("0.3.0");
        Assert(!Directory.Exists(old), "Completed stale update was retained.");
        foreach (var path in new[] { newest, failed, incomplete, future, active, foreign, unknown })
            Assert(Directory.Exists(path), "Pruning removed an ineligible operation.");
    }
    service.PruneCompletedUpdates("0.3.0");
    Assert(!Directory.Exists(active), "Inactive old helper cache was retained.");
    Assert(Directory.Exists(newest) && Directory.Exists(foreign), "Newest backup or user content was removed.");
    Assert(await File.ReadAllTextAsync(System.IO.Path.Combine(foreign, "my-notes.txt")) == "preserve", "Pruning altered user content.");
});
await Test("cache pruning requires safe immediate-child operation boundaries and valid results", async () =>
{
    using var temp = new Temp();
    var data = temp.Path("data"); var cache = System.IO.Path.Combine(data, "manager-updates"); Directory.CreateDirectory(cache);
    var malformed = System.IO.Path.Combine(cache, "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(malformed);
    await File.WriteAllTextAsync(System.IO.Path.Combine(malformed, "update-result.json"), "{}");
    var wrongName = System.IO.Path.Combine(cache, "update-not-a-guid"); Directory.CreateDirectory(wrongName);
    var outside = temp.Path("outside", "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outside);
    await File.WriteAllTextAsync(System.IO.Path.Combine(outside, "update-result.json"), "keep");
    using var service = new ManagerUpdateService(cache);
    service.PruneCompletedUpdates("0.3.0"); service.PruneCompletedUpdates("not a version");
    Assert(Directory.Exists(malformed) && Directory.Exists(wrongName) && Directory.Exists(outside), "Pruning escaped an operation boundary.");
});
await Test("cache pruning never follows Windows junctions, including dangling junctions", async () =>
{
    using var temp = new Temp();
    var data = temp.Path("data"); var cache = System.IO.Path.Combine(data, "manager-updates"); Directory.CreateDirectory(cache);
    var outside = temp.Path("outside"); Directory.CreateDirectory(outside); await File.WriteAllTextAsync(temp.Path("outside", "keep.txt"), "keep");
    var links = new List<string>();
    try
    {
        foreach (var (age, target) in new[] { (1, ""), (10, outside), (20, temp.Path("nonexistent")) })
        {
            var operation = System.IO.Path.Combine(cache, "update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(operation);
            var resultPath = System.IO.Path.Combine(operation, "update-result.json");
            await File.WriteAllBytesAsync(resultPath, JsonSerializer.SerializeToUtf8Bytes(new ManagerUpdateResult(true, false, "manager_update_completed", "", "0.2.0", "0.3.0")));
            File.SetLastWriteTimeUtc(resultPath, DateTime.UtcNow.AddDays(-age));
            if (target.Length == 0) continue;
            var link = System.IO.Path.Combine(operation, "candidate"); TestJunction.Create(link, target); links.Add(link);
            ThrowsSync<IOException>(() => ManagerUpdateService.RejectReparsePath(link));
        }
        using var service = new ManagerUpdateService(cache); service.PruneCompletedUpdates("0.3.0");
        Assert(Directory.EnumerateDirectories(cache).Count() == 3, "An unsafe junction operation was pruned.");
        Assert(await File.ReadAllTextAsync(temp.Path("outside", "keep.txt")) == "keep", "Pruning followed a junction target.");
    }
    finally { foreach (var link in links) Directory.Delete(link, false); }
});
Console.WriteLine($"Manager updater: {count} tests passed. No application, game, process, registry or real network was modified.");

async Task Test(string name, Func<Task> run) { await run(); count++; Console.WriteLine("PASS " + name); }
static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static async Task Throws<T>(Func<Task> run) where T : Exception
{ try { await run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
static void ThrowsSync<T>(Action run) where T : Exception
{ try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
static string Fixture(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
static JsonObject Release()
{
    var tag = "v0.3.0";
    var assets = new JsonArray();
    foreach (var name in new[] { ManagerUpdateService.PortableAssetName, ManagerUpdateService.InstallerAssetName })
        assets.Add(new JsonObject { ["name"] = name, ["size"] = 1000, ["digest"] = "sha256:" + new string('a', 64),
            ["browser_download_url"] = "https://github.com/" + ManagerUpdateService.Repository + "/releases/download/" + tag + "/" + name });
    return new JsonObject { ["draft"] = false, ["prerelease"] = false, ["tag_name"] = tag,
        ["html_url"] = "https://github.com/" + ManagerUpdateService.Repository + "/releases/tag/" + tag, ["assets"] = assets };
}
static void Zip(string path, IEnumerable<(string Name, string Content, int Attributes)> files)
{
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var file in files)
    {
        var entry = archive.CreateEntry(file.Name); entry.ExternalAttributes = file.Attributes;
        using var writer = new StreamWriter(entry.Open()); writer.Write(file.Content);
    }
}
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return send(request, token); } }
static class TestJunction
{
    // Directory junctions need no administrator/developer-mode privilege. All paths belong to this test's GUID temp directory.
    public static void Create(string link, string target)
    {
        Directory.CreateDirectory(link);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + System.IO.Path.GetFullPath(target) + "\0");
        var print = Encoding.Unicode.GetBytes(System.IO.Path.GetFullPath(target) + "\0");
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write(0xA0000003u); writer.Write(checked((ushort)(8 + substitute.Length + print.Length))); writer.Write((ushort)0);
        writer.Write((ushort)0); writer.Write(checked((ushort)(substitute.Length - 2)));
        writer.Write(checked((ushort)substitute.Length)); writer.Write(checked((ushort)(print.Length - 2)));
        writer.Write(substitute); writer.Write(print); writer.Flush();
        using var handle = CreateFile(link, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, buffer.ToArray(), checked((uint)buffer.Length), IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("Could not create fixture junction.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, IntPtr output,
        uint outputSize, out uint returned, IntPtr overlapped);
}
sealed class Temp : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DLSSG-ManagerUpdateTests-" + Guid.NewGuid().ToString("N"));
    public Temp() => Directory.CreateDirectory(Root);
    public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);
    public void Dispose()
    {
        var root = System.IO.Path.GetFullPath(Root);
        if (!root.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !System.IO.Path.GetFileName(root).StartsWith("DLSSG-ManagerUpdateTests-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
        ManagerUpdateService.RejectReparsePath(root); Directory.Delete(root, true);
    }
}
sealed class UpdateFixture : IDisposable
{
    private readonly Temp _temp = new();
    public string Root => _temp.Root;
    public ManagerUpdatePlan Plan { get; private set; } = null!;
    public Dictionary<string, string> OriginalHashes { get; } = new();
    public string Path(params string[] parts) => _temp.Path(parts);
    public static async Task<UpdateFixture> Create()
    {
        var fixture = new UpdateFixture();
        var data = fixture.Path("data");
        var operation = System.IO.Path.Combine(data, "manager-updates", "update-" + Guid.NewGuid().ToString("N"));
        var target = fixture.Path("target"); var candidate = System.IO.Path.Combine(operation, "candidate");
        var worker = System.IO.Path.Combine(operation, "worker", ManagerUpdateService.ExeName);
        Directory.CreateDirectory(target); Directory.CreateDirectory(candidate); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(worker)!);
        var files = new List<ManagerUpdateFile>();
        foreach (var name in ManagerUpdateService.OwnedFiles)
        {
            var oldPath = System.IO.Path.Combine(target, name); var newPath = System.IO.Path.Combine(candidate, name);
            if (name == ManagerUpdateService.ExeName)
            {
                File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Old.exe"), oldPath);
                File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "New.exe"), newPath);
            }
            else { await File.WriteAllTextAsync(oldPath, "old:" + name); await File.WriteAllTextAsync(newPath, "new:" + name); }
            fixture.OriginalHashes[name] = await ManagerUpdateService.HashAsync(oldPath, default);
            files.Add(new(name, new FileInfo(newPath).Length, await ManagerUpdateService.HashAsync(newPath, default)));
        }
        await File.WriteAllTextAsync(fixture.Path("target", "unrelated.txt"), "keep");
        File.Copy(System.IO.Path.Combine(target, ManagerUpdateService.ExeName), worker);
        using var current = Process.GetCurrentProcess();
        var plan = new ManagerUpdatePlan
        {
            PlanPath = System.IO.Path.Combine(operation, "update-plan.json"), ResultPath = System.IO.Path.Combine(operation, "update-result.json"),
            DataDirectory = data, OperationDirectory = operation, CandidateDirectory = candidate, WorkerExePath = worker,
            TargetExePath = System.IO.Path.Combine(target, ManagerUpdateService.ExeName), WorkerSha256 = fixture.OriginalHashes[ManagerUpdateService.ExeName],
            OriginalExeSha256 = fixture.OriginalHashes[ManagerUpdateService.ExeName], FromVersion = "0.2.0", ToVersion = "0.3.0",
            ParentProcessId = Environment.ProcessId, ParentStartedUtcTicks = current.StartTime.ToUniversalTime().Ticks,
            Mode = ManagerUpdateMode.Portable, Files = files, LaunchArguments = ["--data-dir", data, "--game", "sample game"]
        };
        var raw = JsonSerializer.SerializeToUtf8Bytes(plan, ManagerUpdateService.Json);
        await File.WriteAllBytesAsync(plan.PlanPath, raw);
        fixture.Plan = plan with { PlanSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() };
        return fixture;
    }
    public void Dispose() => _temp.Dispose();
}

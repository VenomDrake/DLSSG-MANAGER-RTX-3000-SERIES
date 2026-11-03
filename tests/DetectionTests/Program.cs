using System.Security.Cryptography;
using System.Text.Json;
using DlssgManager.Core;

// Offline, self-contained tests. No real game, hardware, user profile, registry, or Steam library is inspected.
// DLL fixtures are inspected as files only: a locally compiled inert assembly and a copy of the current .NET runtime metadata.
int passed = 0;
void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL " + name);
    passed++;
    Console.WriteLine("PASS " + name);
}

using var sandbox = new TestSandbox();
string runtimeFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nvngx_dlssg.dll");
Assert(File.Exists(runtimeFixture), "synthetic runtime fixture was built and copied");
var scanner = new GameScanner();
var package = MakePackage(sandbox.PathFor("package"), runtimeFixture);
string game = sandbox.PathFor("SyntheticGame_游戏_اختبار");
string exe = Path.Combine(game, "Project", "Binaries", "Win64", "Synthetic-Win64-Shipping.exe");
WritePe(exe);
string nativeRuntime = Path.Combine(game, "Project", "Plugins", "Streamline", "Binaries", "Win64", "nvngx_dlssg.dll");
Directory.CreateDirectory(Path.GetDirectoryName(nativeRuntime)!);
File.Copy(runtimeFixture, nativeRuntime);

var scan = await scanner.ScanAsync(game, package);
Assert(scan.RenderingExe == Path.GetFullPath(exe) && scan.CanInstall, "UE rendering executable and synthetic generation B runtime recognized");
Assert(scan.RuntimeVersion == "310.9.16.0", "runtime version is read from PE metadata");
Assert(scan.Checks.Any(c => c.Code == "anticheat" && c.Level == CheckLevel.Warning), "absent anti-cheat indicators are not claimed as certainty");
Assert(scan.Checks.Any(c => c.Code == "dx12_unverified" && c.Level == CheckLevel.Warning), "runtime presence alone does not prove DirectX 12");
var fromExe = await scanner.ScanAsync(exe, package);
Assert(fromExe.RuntimePath == nativeRuntime && fromExe.CanInstall, "manual Unreal EXE selection searches project plugins");
var missingPackage = await scanner.ScanAsync(game);
Assert(!missingPackage.CanInstall && missingPackage.Checks.Any(c => c.Code == "package_ready" && c.Level == CheckLevel.Blocked), "no verified package blocks installation");

string target = Path.GetDirectoryName(exe)!;
string proxy = Path.Combine(target, "version.dll");
File.WriteAllText(proxy, "unknown loader");
var conflict = await scanner.ScanAsync(game, package);
Assert(!conflict.CanInstall && conflict.ModState == ModState.UnknownConflict, "unknown proxy blocks installation");
File.Delete(proxy);

foreach (var entry in package.Files.Where(f => f.Path == "version.dll" || f.Path.StartsWith("dlssg_sm86/", StringComparison.Ordinal)))
{
    string destination = Path.Combine(target, entry.Path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(Path.Combine(package.Directory, entry.Path.Replace('/', Path.DirectorySeparatorChar)), destination);
}
var manual = await scanner.ScanAsync(game, package);
Assert(manual.ModState == ModState.RecognizedManual && manual.InstalledVersion == "0.4.1" && manual.CanInstall, "manual installation is recognized from verified file hashes");
File.WriteAllText(Path.Combine(target, "winmm.dll"), "second loader");
var multipleProxy = await scanner.ScanAsync(game, package);
Assert(!multipleProxy.CanInstall && multipleProxy.ModState == ModState.UnknownConflict, "a second loader prevents manual adoption");
File.Delete(Path.Combine(target, "winmm.dll"));
string backend = Path.Combine(target, "dlssg_sm86", "backend", "sm86_backend.dll");
File.AppendAllText(backend, "changed");
var modified = await scanner.ScanAsync(game, package);
Assert(!modified.CanInstall && modified.ModState == ModState.UnknownConflict, "changed mod binary fails manual hash recognition");
File.Copy(Path.Combine(package.Directory, "dlssg_sm86", "backend", "sm86_backend.dll"), backend, true);

string record = Path.Combine(target, ".dlssg-manager.json");
File.WriteAllText(record, "not valid json");
var corruptRecord = await scanner.ScanAsync(game, package);
Assert(!corruptRecord.CanInstall && corruptRecord.Checks.Any(c => c.Code == "mod_record" && c.Level == CheckLevel.Blocked), "corrupt manager record blocks changes");
WriteRecord(record, target, package, false);
var managed = await scanner.ScanAsync(game, package);
Assert(managed.ModState == ModState.Managed && managed.CanInstall, "valid manager record and hashes recognized");
WriteRecord(record, target, package, true);
var traversal = await scanner.ScanAsync(game, package);
Assert(!traversal.CanInstall && traversal.Checks.Any(c => c.Code == "mod_record" && c.Level == CheckLevel.Blocked), "record path traversal is rejected");
File.Delete(record);

Directory.CreateDirectory(Path.Combine(game, "EasyAntiCheat"));
var anti = await scanner.ScanAsync(game, package);
Assert(!anti.CanInstall && anti.Checks.Any(c => c.Code == "anticheat" && c.Level == CheckLevel.Blocked), "known anti-cheat marker blocks installation");

string ambiguous = sandbox.PathFor("Ambiguous");
string one = Path.Combine(ambiguous, "One.exe"), two = Path.Combine(ambiguous, "Two.exe");
WritePe(one);
WritePe(two);
File.Copy(runtimeFixture, Path.Combine(ambiguous, "nvngx_dlssg.dll"));
var ambiguity = await scanner.ScanAsync(ambiguous, package);
Assert(ambiguity.RenderingExe is null && !ambiguity.CanInstall, "multiple executable candidates require explicit choice");
var selected = await scanner.ScanAsync(ambiguous, package, two);
Assert(selected.CanInstall && selected.RenderingExe == two, "explicit rendering executable resolves ambiguity");
var outside = await scanner.ScanAsync(ambiguous, package, exe);
Assert(outside.RenderingExe is null && !outside.CanInstall, "rendering executable cannot escape the selected game root");
WritePe(two, 0x14c);
var x86 = await scanner.ScanAsync(ambiguous, package, two);
Assert(!x86.CanInstall && x86.Checks.Any(c => c.Code == "exe_x64" && c.Level == CheckLevel.Blocked), "x86 executable blocked");
WritePe(two);
File.Copy(typeof(object).Assembly.Location, Path.Combine(ambiguous, "nvngx_dlssg.dll"), true);
var oldRuntime = await scanner.ScanAsync(ambiguous, package, two);
Assert(!oldRuntime.CanInstall && oldRuntime.RuntimeVersion is not null && !oldRuntime.RuntimeVersion.StartsWith("310.", StringComparison.Ordinal)
    && oldRuntime.Checks.Any(c => c.Code == "runtime_generation" && c.Level == CheckLevel.Blocked), "known non-310 runtime generation blocked");
File.WriteAllBytes(Path.Combine(ambiguous, "nvngx_dlssg.dll"), []);
var emptyRuntime = await scanner.ScanAsync(ambiguous, package, two);
Assert(!emptyRuntime.CanInstall && emptyRuntime.RuntimeVersion is null, "empty native runtime keeps installation blocked with unknown metadata");
File.WriteAllText(Path.Combine(ambiguous, "nvngx_dlssg.dll"), "no version metadata");
var unknownRuntime = await scanner.ScanAsync(ambiguous, package, two);
Assert(!unknownRuntime.CanInstall && unknownRuntime.Checks.Any(c => c.Code == "runtime_generation" && c.Level == CheckLevel.Blocked), "unknown runtime version blocked");
File.Delete(Path.Combine(ambiguous, "nvngx_dlssg.dll"));
var missingRuntime = await scanner.ScanAsync(ambiguous, package, two);
Assert(!missingRuntime.CanInstall && missingRuntime.Checks.Any(c => c.Code == "game_dlssg" && c.Level == CheckLevel.Blocked), "missing native FG runtime blocked");
var ambiguousMissingRuntime = await scanner.ScanAsync(ambiguous, package);

void AssertSupport(GameScan result, GameSupportState expected, string reason, string name)
{
    var assessment = GameSupportAssessment.Evaluate(result);
    Assert(assessment.State == expected && assessment.ReasonKeys.Contains(reason, StringComparer.Ordinal), name);
}
AssertSupport(scan, GameSupportState.RequirementsDetected, "support_reason_ready", "detected runtime requirements do not claim verified game compatibility");
AssertSupport(fromExe, GameSupportState.RequirementsDetected, "support_reason_ready", "manual executable scan receives a requirements-only assessment");
AssertSupport(managed, GameSupportState.RequirementsDetected, "support_reason_ready", "managed installation does not imply a new compatibility guarantee");
AssertSupport(manual, GameSupportState.RequirementsDetected, "support_reason_ready", "recognized manual installation keeps file-based assessment");
AssertSupport(missingPackage, GameSupportState.InstallationBlocked, "support_reason_other", "missing package blocks installation without declaring the game unsupported");
AssertSupport(conflict, GameSupportState.InstallationBlocked, "support_reason_other", "proxy conflict is distinguished from unsupported game requirements");
AssertSupport(corruptRecord, GameSupportState.InstallationBlocked, "support_reason_other", "corrupt management record is an installation blocker");
AssertSupport(anti, GameSupportState.InstallationBlocked, "support_reason_anticheat", "anti-cheat marker clearly blocks installation");
AssertSupport(ambiguity, GameSupportState.NeedsReview, "support_reason_exe", "ambiguous rendering executable asks for review rather than declaring unsupported");
AssertSupport(outside, GameSupportState.NeedsReview, "support_reason_exe", "out-of-root executable selection remains a selection problem");
AssertSupport(selected, GameSupportState.RequirementsDetected, "support_reason_ready", "explicit rendering executable resolves the support assessment");
AssertSupport(x86, GameSupportState.Unsupported, "support_reason_architecture", "x86 rendering executable gives a clear architecture reason");
AssertSupport(oldRuntime, GameSupportState.Unsupported, "support_reason_runtime", "non-310 runtime explains the unsupported requirement");
AssertSupport(emptyRuntime, GameSupportState.NeedsReview, "support_reason_runtime_unknown", "empty runtime metadata asks for review without claiming an unsupported generation");
AssertSupport(unknownRuntime, GameSupportState.NeedsReview, "support_reason_runtime_unknown", "corrupt runtime metadata asks for review without claiming an unsupported generation");
AssertSupport(missingRuntime, GameSupportState.Unsupported, "support_reason_no_runtime", "complete scan with no native runtime explains why the mod is unsupported");
AssertSupport(ambiguousMissingRuntime, GameSupportState.Unsupported, "support_reason_no_runtime", "complete multi-executable folder without a native runtime is unsupported before executable selection");
var badPath = await scanner.ScanAsync(sandbox.PathFor("DoesNotExist"), package);
AssertSupport(badPath, GameSupportState.NeedsReview, "support_reason_scan", "invalid game path cannot establish that a game is unsupported");
string noExecutableDirectory = sandbox.PathFor("NoExecutable"); Directory.CreateDirectory(noExecutableDirectory);
var noExecutable = await scanner.ScanAsync(noExecutableDirectory, package);
AssertSupport(noExecutable, GameSupportState.Unsupported, "support_reason_no_runtime", "complete empty folder has no native runtime regardless of executable availability");
File.Copy(runtimeFixture, Path.Combine(noExecutableDirectory, "nvngx_dlssg.dll"));
var noExecutableWithRuntime = await scanner.ScanAsync(noExecutableDirectory, package);
AssertSupport(noExecutableWithRuntime, GameSupportState.NeedsReview, "support_reason_no_exe", "folder with a runtime but no executable asks for the correct game folder");
var partialMissingRuntime = missingRuntime with { Checks = missingRuntime.Checks.Select(c => c.Code == "game_scan" ? c with { Level = CheckLevel.Blocked } : c).ToArray() };
AssertSupport(partialMissingRuntime, GameSupportState.NeedsReview, "support_reason_scan", "incomplete scan cannot use a missing runtime to declare a game unsupported");
var unstarted = new GameScan("synthetic", null, [], null, null, ModState.NotInstalled, null, []);
AssertSupport(unstarted, GameSupportState.NeedsReview, "support_reason_scan", "empty synthetic scan is incomplete rather than unsupported");
var missingChecks = scan with { Checks = [new("game_scan", CheckLevel.Pass), new("game_dlssg", CheckLevel.Pass)] };
AssertSupport(missingChecks, GameSupportState.NeedsReview, "support_reason_scan", "missing technical checks prevent a premature unsupported assessment");
var uncertainArchitecture = scan with { Checks = scan.Checks.Select(c => c.Code == "exe_x64" ? c with { Level = CheckLevel.Unknown } : c).ToArray() };
AssertSupport(uncertainArchitecture, GameSupportState.NeedsReview, "support_reason_scan", "unknown architecture is a review requirement rather than a technical rejection");
string lockedDirectory = sandbox.PathFor("LockedExecutable");
string lockedExe = Path.Combine(lockedDirectory, "LockedGame.exe"); WritePe(lockedExe);
File.Copy(runtimeFixture, Path.Combine(lockedDirectory, "nvngx_dlssg.dll"));
using (var lockHandle = new FileStream(lockedExe, FileMode.Open, FileAccess.Read, FileShare.None))
{
    var lockedExecutable = await scanner.ScanAsync(lockedDirectory, package);
    Assert(!lockedExecutable.CanInstall && lockedExecutable.Checks.Any(c => c.Code == "exe_inspection" && c.Level == CheckLevel.Blocked)
        && !lockedExecutable.Checks.Any(c => c.Code == "exe_x64"), "locked executable blocks inspection without pretending its architecture is known");
    AssertSupport(lockedExecutable, GameSupportState.NeedsReview, "support_reason_scan", "locked executable asks for review rather than being labeled x86 or unsupported");
}
File.WriteAllText(lockedExe, "corrupt executable fixture");
var corruptExecutable = await scanner.ScanAsync(lockedDirectory, package);
Assert(!corruptExecutable.CanInstall && corruptExecutable.Checks.Any(c => c.Code == "exe_inspection" && c.Level == CheckLevel.Blocked), "corrupt executable remains blocked by inspection failure");
AssertSupport(corruptExecutable, GameSupportState.NeedsReview, "support_reason_scan", "corrupt executable does not establish an unsupported architecture");
AssertSupport(unknownRuntime with { RuntimeVersion = " ? " }, GameSupportState.NeedsReview, "support_reason_runtime_unknown", "explicit unknown runtime marker remains a review requirement");
var antiAndUnsupported = missingRuntime with { Checks = missingRuntime.Checks.Append(new("anticheat", CheckLevel.Blocked)).ToArray() };
var multipleReasons = GameSupportAssessment.Evaluate(antiAndUnsupported);
Assert(multipleReasons.State == GameSupportState.Unsupported && multipleReasons.ReasonKeys.Contains("support_reason_no_runtime")
    && multipleReasons.ReasonKeys.Contains("support_reason_anticheat"), "technical rejection preserves the separate anti-cheat blocker reason");
var runningSynthetic = scan with { Checks = scan.Checks.Select(c => c.Code == "game_closed" ? c with { Level = CheckLevel.Blocked } : c).ToArray() };
AssertSupport(runningSynthetic, GameSupportState.InstallationBlocked, "support_reason_other", "running game blocks installation without being labeled unsupported");
Assert(scan.CanInstall && !missingRuntime.CanInstall && !ambiguity.CanInstall, "support assessment leaves CanInstall safety decisions unchanged");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    bool observedCancellation = false;
    try { await scanner.ScanAsync(game, package, cancellationToken: canceled.Token); }
    catch (OperationCanceledException) { observedCancellation = true; }
    Assert(observedCancellation, "scanner honors cancellation");
}

string log = sandbox.PathFor("session.log");
Assert(LogReader.Read(log).Summary == "log_missing", "missing log has no verification");
string Row(int pid, string json, char level = 'I', string exeName = "Synthetic.exe") =>
    $"2026-01-01 00:00:00.000  pid {pid}  {exeName}  {level}  {json}\n";
string Success(int pid) => Row(pid, "{\"event\":\"log_session\"}") +
    Row(pid, "{\"event\":\"install\",\"active\":true,\"gpu\":\"Synthetic GPU\"}") +
    Row(pid, "{\"event\":\"fg_gate_create_feature\",\"feature_id\":11,\"succeeded\":true}") +
    Row(pid, "{\"event\":\"fg_gate_summary\",\"reason\":\"first_evaluate\"}");
File.WriteAllText(log, Success(1));
var first = LogReader.Read(log, "Synthetic.exe");
Assert(first.ModLoaded && first.FrameGenerationCreated && first.Evaluated && first.ProcessId == 1, "current session confirms FG creation and first evaluation");
File.AppendAllText(log, Row(2, "{\"event\":\"log_session\"}") + Row(2, "{\"event\":\"install\",\"active\":true}") +
    Row(2, "{\"event\":\"fg_gate_create_feature\",\"feature_id\":1,\"succeeded\":true}") +
    Row(2, "{\"event\":\"fg_gate_summary\",\"reason\":\"first_evaluate\"}"));
var latest = LogReader.Read(log, "Synthetic.exe");
Assert(latest.ProcessId == 2 && latest.ModLoaded && !latest.Evaluated && !latest.FrameGenerationCreated, "old success and super-resolution-only calls cannot verify a new session");
File.AppendAllText(log, Row(2, "{\"event\":\"fg_gate_environment\",\"hags\":{\"enabled\":true}}") +
    Row(2, "{\"event\":\"fg_gate_create_feature\",\"feature_id\":\"invalid\"}") + "incomplete log line {\n");
var malformed = LogReader.Read(log);
Assert(malformed.HagsActive == true && !malformed.Evaluated, "runtime HAGS parsed while malformed fields and incomplete lines are tolerated");
File.AppendAllText(log, Row(2, "{\"event\":\"synthetic_error\"}", 'E'));
Assert(LogReader.Read(log).Errors.Count == 1 && LogReader.Read(log).Summary == "log_errors", "current-session errors reported");
File.AppendAllText(log, Row(3, "{\"event\":\"log_session\"}", exeName: "Other.exe"));
Assert(LogReader.Read(log, "Synthetic.exe").ProcessId == 2, "EXE filtering excludes unrelated processes");
File.AppendAllText(log, Row(1, "{\"event\":\"log_session\"}") + Row(1, "{\"event\":\"install\",\"active\":true}"));
var reused = LogReader.Read(log, "Synthetic.exe");
Assert(reused.ProcessId == 1 && !reused.Evaluated && reused.Errors.Count == 0, "session boundary protects against reused PIDs and earlier errors");
Console.WriteLine($"All {passed} detection/log checks passed using synthetic fixtures only.");

static ReleasePackage MakePackage(string directory, string fixture)
{
    Directory.CreateDirectory(directory);
    string[] paths = ["version.dll", "dlssg_sm86.ini", "dlssg_sm86/backend/sm86_backend.dll", "dlssg_sm86/kernels/fg_kernels.dll", "dlssg_sm86/runtime/nvngx_dlssg.dll"];
    var files = new List<PackageFile>();
    foreach (string path in paths)
    {
        string destination = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (path.EndsWith("runtime/nvngx_dlssg.dll", StringComparison.Ordinal)) File.Copy(fixture, destination);
        else File.WriteAllText(destination, "Synthetic fixture: " + path);
        using var stream = File.OpenRead(destination);
        files.Add(new(path, stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
    }
    var release = new ReleaseInfo("0.4.1", 1, 1, "synthetic-test-package.zip", "https://example.invalid/fixture", 0, new string('0', 64), DateTimeOffset.UnixEpoch, "https://example.invalid/fixture");
    return new(release, directory, files, true, []);
}

static void WritePe(string path, ushort machine = 0x8664)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var stream = File.Create(path);
    stream.SetLength(512);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0x5a4d);
    stream.Position = 0x3c; writer.Write(0x80);
    stream.Position = 0x80; writer.Write(0x00004550u); writer.Write(machine); writer.Write((ushort)0);
    stream.Position = 0x80 + 20; writer.Write((ushort)0xf0);
    stream.Position = 0x80 + 24; writer.Write((ushort)0x20b);
}

static void WriteRecord(string record, string target, ReleasePackage package, bool traversal)
{
    var entries = package.Files.Select(f => new { Path = f.Path, f.Sha256, f.Size }).ToList();
    if (traversal) entries.Add(new { Path = "../../outside.txt", Sha256 = new string('0', 64), Size = 0L });
    File.WriteAllText(record, JsonSerializer.Serialize(new { SchemaVersion = 1, Version = "0.4.1", GameDirectory = target, Proxy = "version.dll", Files = entries }));
}

sealed class TestSandbox : IDisposable
{
    private const string Prefix = "DlssgManager.DetectionTests-";
    private readonly string root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
    public TestSandbox() => Directory.CreateDirectory(root);
    public string PathFor(string relative) => Path.Combine(root, relative);
    public void Dispose()
    {
        // Verify the resolved recursive-delete target is the exact newly-created test directory.
        string absolute = Path.GetFullPath(root);
        string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(absolute), temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing unsafe fixture cleanup.");
        if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
    }
}

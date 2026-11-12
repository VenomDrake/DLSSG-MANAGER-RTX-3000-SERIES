namespace DlssgManager.Core;

public enum CheckLevel { Pass, Info, Warning, Blocked, Unknown }
public sealed record CheckResult(string Code, CheckLevel Level, string Detail = "");
public sealed record OperationProgress(string Stage, double? Percent = null, string Detail = "");
public sealed record ReleaseInfo(string Version, long ReleaseId, long AssetId, string AssetName,
    string DownloadUrl, long Size, string Sha256, DateTimeOffset PublishedAt, string ReleaseUrl);
public sealed record PackageFile(string Path, long Size, string Sha256);
public sealed record ReleasePackage(ReleaseInfo Release, string Directory, IReadOnlyList<PackageFile> Files,
    bool SupportedLayout, IReadOnlyList<CheckResult> Checks)
{
    public ModProvider Provider { get; init; } = ModProvider.Sm86;
    public string Variant { get; init; } = "full";
}
public enum UpdateState { Downloaded, UpToDate, OfflineCache, Failed }
public sealed record UpdateResult(UpdateState State, ReleasePackage? Package, string Detail = "");
public sealed record GpuInfo(string Name, string DriverVersion, double? ComputeCapability, bool Eligible);
public sealed record HardwareInfo(IReadOnlyList<GpuInfo> Gpus, bool WindowsSupported, bool? HagsConfigured,
    bool? HagsActive, IReadOnlyList<CheckResult> Checks);
public sealed record ExeCandidate(string Path, string Name, int Score);
public enum ModState { NotInstalled, Managed, RecognizedManual, UnknownConflict }
public sealed record GameScan(string Root, string? RenderingExe, IReadOnlyList<ExeCandidate> Executables,
    string? RuntimePath, string? RuntimeVersion, ModState ModState, string? InstalledVersion,
    IReadOnlyList<CheckResult> Checks)
{
    public ModProvider Provider { get; init; } = ModProvider.Sm86;
    public string? RenderingDirectory => RenderingExe is null ? null : System.IO.Path.GetDirectoryName(RenderingExe);
    public bool CanInstall => RenderingExe is not null && Checks.All(c => c.Level != CheckLevel.Blocked);
}
public sealed record SteamGame(string AppId, string Name, string Directory);
public sealed record LogVerification(bool ModLoaded, bool FrameGenerationCreated, bool Evaluated,
    bool? HagsActive, string? Gpu, int? ProcessId, IReadOnlyList<string> Errors, string Summary)
{
    public long? GeneratedEvaluations { get; init; }
    public long? GenerationErrors { get; init; }
    public int? Multiplier { get; init; }
    public string? LogPath { get; init; }
    public DateTimeOffset? LogTimestamp { get; init; }
    public bool PartialLog { get; init; }
}

public sealed record ModOptions
{
    public ModProvider Provider { get; init; } = ModProvider.Sm86;
    public string AnrPath { get; init; } = "auto";
    public bool AnrSceneCut { get; init; } = true;
    public int AnrNitsPerUnit { get; init; } = 80;
    public string Engine { get; init; } = "DLSS";
    public int Multiplier { get; init; } = 2;
    public int Optimization { get; init; } = 1;
    public string Preset { get; init; } = "A";
    public string SuperResolution { get; init; } = "Off";
    public string FlowResolution { get; init; } = "Middle";
    public string UseHudless { get; init; } = "Auto";
    public int LoggingLevel { get; init; } = 2;
    public bool FrameTiming { get; init; }
    public string Proxy { get; init; } = "version.dll";
}

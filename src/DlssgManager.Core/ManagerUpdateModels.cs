using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("ManagerUpdateTests")]

namespace DlssgManager.Core;

public sealed record ManagerUpdateAsset(string Name, string DownloadUrl, long Size, string Sha256);
public sealed record ManagerUpdateInfo(string Version, string Tag, string ReleaseUrl,
    ManagerUpdateAsset PortableAsset, ManagerUpdateAsset InstallerAsset);
public enum ManagerUpdateMode { Portable, Installed }
public sealed record ManagerUpdateFile(string Name, long Size, string Sha256);
public sealed record ManagerUpdatePlan
{
    public int SchemaVersion { get; init; } = 1;
    public string PlanPath { get; init; } = "";
    [JsonIgnore] public string PlanSha256 { get; init; } = "";
    public string DataDirectory { get; init; } = "";
    public string OperationDirectory { get; init; } = "";
    public string CandidateDirectory { get; init; } = "";
    public string WorkerExePath { get; init; } = "";
    public string WorkerSha256 { get; init; } = "";
    public string TargetExePath { get; init; } = "";
    public string OriginalExeSha256 { get; init; } = "";
    public string ResultPath { get; init; } = "";
    public string FromVersion { get; init; } = "";
    public string ToVersion { get; init; } = "";
    public int ParentProcessId { get; init; }
    public long ParentStartedUtcTicks { get; init; }
    public ManagerUpdateMode Mode { get; init; }
    public IReadOnlyList<ManagerUpdateFile> Files { get; init; } = [];
    public IReadOnlyList<string> LaunchArguments { get; init; } = [];
}
public sealed record ManagerUpdateResult(bool Success, bool RebootRequired, string Code, string Detail,
    string FromVersion, string ToVersion);

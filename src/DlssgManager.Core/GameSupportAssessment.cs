namespace DlssgManager.Core;

public enum GameSupportState { Unsupported, NeedsReview, RequirementsDetected, InstallationBlocked }

/// <summary>Describes file-based prerequisites, without claiming that frame generation works in the game.</summary>
public sealed record GameSupportAssessment(GameSupportState State, IReadOnlyList<string> ReasonKeys)
{
    public static GameSupportAssessment Evaluate(GameScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        bool Has(string code, CheckLevel level) => scan.Checks.Any(c => c.Code == code && c.Level == level);
        bool IsKnown(string code) => Has(code, CheckLevel.Pass) || Has(code, CheckLevel.Blocked);
        string runtimeCheck = scan.Provider == ModProvider.Anr ? "anr_runtime" : "runtime_generation";
        GameSupportAssessment Result(GameSupportState state, params string[] reasons) => new(state, Array.AsReadOnly(reasons));

        // A partial directory scan cannot establish that a required file is absent.
        if (!Has("game_scan", CheckLevel.Pass) || Has("exe_inspection", CheckLevel.Blocked)
            || scan.Checks.Any(c => c.Code is "game_path" or "game_scan" && c.Level == CheckLevel.Blocked))
            return Result(GameSupportState.NeedsReview, "support_reason_scan");
        // Runtime absence applies to the entire fully scanned folder, regardless of executable choice.
        if (Has("game_dlssg", CheckLevel.Blocked))
            return Has("anticheat", CheckLevel.Blocked)
                ? Result(GameSupportState.Unsupported, "support_reason_no_runtime", "support_reason_anticheat")
                : Result(GameSupportState.Unsupported, "support_reason_no_runtime");
        if (Has("game_dlssg", CheckLevel.Pass) && Has(runtimeCheck, CheckLevel.Blocked)
            && (string.IsNullOrWhiteSpace(scan.RuntimeVersion) || scan.RuntimeVersion.Trim() == "?"))
            return Result(GameSupportState.NeedsReview, "support_reason_runtime_unknown");
        if (string.IsNullOrWhiteSpace(scan.RenderingExe))
            return Result(GameSupportState.NeedsReview, scan.Executables.Count > 0 ? "support_reason_exe" : "support_reason_no_exe");
        if (!Has("render_exe", CheckLevel.Pass) || !IsKnown("exe_x64") || !IsKnown("game_dlssg")
            || Has("game_dlssg", CheckLevel.Pass) && !IsKnown(runtimeCheck))
            return Result(GameSupportState.NeedsReview, "support_reason_scan");

        var technicalReasons = new List<string>();
        if (Has("exe_x64", CheckLevel.Blocked)) technicalReasons.Add("support_reason_architecture");
        if (Has(runtimeCheck, CheckLevel.Blocked)) technicalReasons.Add("support_reason_runtime");
        bool antiCheat = Has("anticheat", CheckLevel.Blocked);
        if (technicalReasons.Count > 0)
        {
            if (antiCheat) technicalReasons.Add("support_reason_anticheat");
            return Result(GameSupportState.Unsupported, technicalReasons.ToArray());
        }

        var blockers = new List<string>();
        if (antiCheat) blockers.Add("support_reason_anticheat");
        if (scan.Checks.Any(c => c.Level == CheckLevel.Blocked && c.Code != "anticheat")) blockers.Add("support_reason_other");
        return blockers.Count > 0
            ? Result(GameSupportState.InstallationBlocked, blockers.ToArray())
            : Result(GameSupportState.RequirementsDetected, "support_reason_ready");
    }
}

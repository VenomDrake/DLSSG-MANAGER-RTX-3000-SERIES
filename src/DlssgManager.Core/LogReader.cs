using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

/// <summary>Reads only the last process session, never treating an old session's success as a new test.</summary>
public static class LogReader
{
    private sealed record Row(int Pid, string Exe, char Level, JsonElement Data);
    public static LogVerification Read(string logPath, string? exeName = null)
    {
        var rows = new List<Row>();
        try
        {
            using var file = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool tailOnly = file.Length > 4_194_304;
            if (tailOnly) file.Position = file.Length - 4_194_304;
            using var reader = new StreamReader(file, Encoding.UTF8, true, 4096);
            if (tailOnly) reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length > 131072) continue;
                var match = Regex.Match(line, @"\bpid\s+(\d+)\s+(.+?)\s+([EID])\s+(\{.*\})$", RegexOptions.CultureInvariant);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int pid)) continue;
                string rowExe = match.Groups[2].Value.Trim();
                if (exeName is not null && !Path.GetFileName(rowExe).Equals(Path.GetFileName(exeName), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var json = JsonDocument.Parse(match.Groups[4].Value);
                    rows.Add(new(pid, rowExe, match.Groups[3].Value[0], json.RootElement.Clone()));
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return new(false, false, false, null, null, null, [], "log_missing"); }
        if (rows.Count == 0) return new(false, false, false, null, null, null, [], "log_empty");
        int sessionIndex = rows.FindLastIndex(r => GetString(r.Data, "event") == "log_session");
        int currentPid = sessionIndex >= 0 ? rows[sessionIndex].Pid : rows[^1].Pid;
        bool loaded = false, created = false, evaluated = false;
        bool? hags = null;
        string? gpu = null;
        var errors = new List<string>();
        for (int i = Math.Max(0, sessionIndex); i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Pid != currentPid) continue;
            var data = row.Data;
            string? evt = GetString(data, "event");
            if (evt == "install")
            {
                loaded = GetBool(data, "active") == true;
                gpu = GetString(data, "gpu") ?? gpu;
            }
            if (evt == "fg_gate_environment" && data.TryGetProperty("hags", out var hagsData))
                hags = GetBool(hagsData, "enabled") ?? (GetString(hagsData, "state") switch { "on" => true, "off" => false, _ => null });
            if (evt == "fg_gate_hags_disabled") hags = false;
            if (evt == "fg_gate_create_feature" && GetInt(data, "feature_id") == 11)
                created = GetBool(data, "succeeded") == true;
            if (created && evt == "fg_gate_summary" && GetString(data, "reason") == "first_evaluate") evaluated = true;
            if (created && evt == "fg_gate_exit" && GetInt(data, "evaluates") > 0) evaluated = true;
            if (created && evt is "evaluate" or "fg_evaluate" or "fg_gate_evaluate") evaluated = true;
            if (row.Level == 'E')
            {
                if (errors.Count == 100) errors.RemoveAt(0);
                errors.Add(data.GetRawText());
            }
        }
        string summary = errors.Count > 0 ? "log_errors" : evaluated ? "log_verified" : created ? "log_created" : loaded ? "log_loaded" : "log_waiting";
        return new(loaded, created, evaluated, hags, gpu, currentPid, errors, summary);
    }
    private static string? GetString(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool? GetBool(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static int? GetInt(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
}

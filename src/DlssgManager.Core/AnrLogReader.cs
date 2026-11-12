using System.Text;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

/// <summary>Reads only the latest ANR log file and its final provider session. Never runs a downloaded binary.</summary>
public static class AnrLogReader
{
    private const long MaximumLogBytes = 8L * 1024 * 1024;
    public static LogVerification ReadLatest(string renderingDirectory, string? exeName = null)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(renderingDirectory));
            if (exeName is not null && Path.IsPathRooted(exeName)
                && !string.Equals(Path.GetDirectoryName(Path.GetFullPath(exeName)), root, StringComparison.OrdinalIgnoreCase))
                return Missing();
            var directory = Path.Combine(root, "OptiScaler", "custom", "Apex Neural Rendering FG", "dlssg");
            if (!Directory.Exists(directory) || DetectionPaths.HasReparsePoint(directory, root)) return Missing();
            var file = Directory.EnumerateFiles(directory, "nr_fg-*.log", SearchOption.TopDirectoryOnly)
                .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^nr_fg-\d{8}-\d{6}(?:[-_]\d+)?\.log$", RegexOptions.CultureInvariant))
                .OrderDescending(StringComparer.Ordinal).FirstOrDefault();
            if (file is null || DetectionPaths.HasReparsePoint(file, root)) return Missing();
            return Read(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Missing(); }
    }

    public static LogVerification Read(string logPath)
    {
        bool loaded = false, created = false, ready = false, evaluated = false;
        string? gpu = null;
        var errors = new List<string>();
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool tail = stream.Length > MaximumLogBytes;
            if (tail) stream.Position = stream.Length - MaximumLogBytes;
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096);
            if (tail) reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length > 131072) continue;
                var match = Regex.Match(line, @"^\[\d{2}:\d{2}:\d{2}\.\d{3}\]\s+\[(info|warning|warn|error|fatal)\]\s+(.+)$", RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var message = match.Groups[2].Value;
                if (message.StartsWith("nr_fg DLSS-G provider in ", StringComparison.Ordinal))
                {
                    loaded = true; created = ready = evaluated = false; gpu = null; errors.Clear();
                    continue;
                }
                if (!loaded) continue;
                if (message.StartsWith("frame generation feature: ", StringComparison.Ordinal))
                { created = true; ready = evaluated = false; }
                if (created && message.StartsWith("our frame generation ready for ", StringComparison.Ordinal)) ready = true;
                var gpuLine = Regex.Match(message, @"^GPU:\s+(.+?),\s+Vulkan\b", RegexOptions.CultureInvariant);
                if (gpuLine.Success) gpu = gpuLine.Groups[1].Value;
                // The observed B15 GPU summary explicitly counts generated evaluations and generation errors.
                // An evaluate entry, pipeline load, or ready message alone is not proof of generated frames.
                var summary = Regex.Match(message, @"\((\d+) generated evaluations, (\d+) generation errors\)", RegexOptions.CultureInvariant);
                if (summary.Success && long.TryParse(summary.Groups[1].Value, out long generated)
                    && long.TryParse(summary.Groups[2].Value, out long failed))
                {
                    if (created && ready && generated > 0 && failed == 0) evaluated = true;
                    if (failed > 0) { evaluated = false; AddError(message); }
                }
                if (match.Groups[1].Value is "error" or "fatal") { evaluated = false; AddError(message); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return Missing(); }
        var status = errors.Count > 0 ? "log_errors" : evaluated ? "log_verified" : created ? "log_created" : loaded ? "log_loaded" : "log_empty";
        return new(loaded, created, evaluated && errors.Count == 0, null, gpu, null, errors, status);

        void AddError(string message)
        {
            if (errors.Count == 100) errors.RemoveAt(0);
            errors.Add(message);
        }
    }

    private static LogVerification Missing() => new(false, false, false, null, null, null, [], "log_missing");
}

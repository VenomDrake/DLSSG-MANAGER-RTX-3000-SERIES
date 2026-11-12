using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DlssgManager.Core;

/// <summary>Reads only the latest ANR log file and its final provider session. Never runs a downloaded binary.</summary>
public static class AnrLogReader
{
    private const long MaximumLogBytes = 8L * 1024 * 1024;
    private const int MaximumLineBytes = 131072;
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
        bool partial = false;
        string? gpu = null;
        long? generatedEvaluations = null, generationErrors = null;
        int? multiplier = null;
        var errors = new List<string>();
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Freeze the byte limit at open: a game appending to the file cannot keep this
            // synchronous UI read running indefinitely. A bounded tail cannot prove context
            // omitted before it, so it stays partial until a complete provider header appears.
            long length = stream.Length;
            partial = length > MaximumLogBytes;
            bool skipFirst = false;
            if (partial)
            {
                long start = length - MaximumLogBytes;
                stream.Position = start - 1;
                skipFirst = stream.ReadByte() != (byte)'\n';
                stream.Position = start;
            }
            foreach (var line in ReadCompleteLines(stream, length - stream.Position, skipFirst))
            {
                if (line is null)
                {
                    // An oversized line could hide a session boundary or error. Discard the
                    // untrustworthy context, rather than retaining an earlier green status.
                    loaded = created = ready = evaluated = false;
                    gpu = null; generatedEvaluations = generationErrors = null; multiplier = null;
                    partial = true;
                    continue;
                }
                var match = Regex.Match(line, @"^\[\d{2}:\d{2}:\d{2}\.\d{3}\]\s+\[(info|warning|warn|error|fatal)\]\s+(.+)$", RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var message = match.Groups[2].Value;
                if (message.StartsWith("nr_fg DLSS-G provider in ", StringComparison.Ordinal))
                {
                    loaded = true; created = ready = evaluated = false; gpu = null; errors.Clear();
                    generatedEvaluations = generationErrors = null; multiplier = null; partial = false;
                    continue;
                }
                if (!loaded) continue;
                if (message.StartsWith("frame generation feature: ", StringComparison.Ordinal))
                { created = true; ready = evaluated = false; generatedEvaluations = generationErrors = null; multiplier = null; }
                if (created && message.StartsWith("our frame generation ready for ", StringComparison.Ordinal)) ready = true;
                var gpuLine = Regex.Match(message, @"^GPU:\s+(.+?),\s+Vulkan\b", RegexOptions.CultureInvariant);
                if (gpuLine.Success) gpu = gpuLine.Groups[1].Value;
                var requestedMultiplier = Regex.Match(message, @"^multi-frame count [1-5] \(([2-6])X\) from evaluate \d+\b", RegexOptions.CultureInvariant);
                if (created && requestedMultiplier.Success) multiplier = int.Parse(requestedMultiplier.Groups[1].Value, CultureInfo.InvariantCulture);
                // The observed B15 GPU summary explicitly counts generated evaluations and generation errors.
                // An evaluate entry, pipeline load, or ready message alone is not proof of generated frames.
                bool summaryLine = (message.StartsWith("GPU ms", StringComparison.Ordinal) || message.StartsWith("final GPU ms", StringComparison.Ordinal))
                    && message.Contains("generated evaluations", StringComparison.Ordinal);
                if (summaryLine)
                {
                    evaluated = false;
                    generatedEvaluations = generationErrors = null;
                    multiplier = null;
                    var summary = Regex.Match(message, @"\b([2-6])X \((\d+) generated evaluations, (\d+) generation errors\)\s*$", RegexOptions.CultureInvariant);
                    if (summary.Success) multiplier = int.Parse(summary.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (summary.Success && long.TryParse(summary.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long generated)
                        && long.TryParse(summary.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long failed))
                    {
                        generatedEvaluations = generated; generationErrors = failed;
                        evaluated = created && ready && generated > 0 && failed == 0;
                        if (failed > 0) AddError(message);
                    }
                }
                if (match.Groups[1].Value is "error" or "fatal") { evaluated = false; AddError(message); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return Missing(); }
        var status = errors.Count > 0 ? "log_errors" : evaluated ? "log_verified" : created ? "log_created" : loaded ? "log_loaded" : "log_empty";
        return new(loaded, created, evaluated && errors.Count == 0 && !partial, null, gpu, null, errors, status)
        {
            GeneratedEvaluations = generatedEvaluations, GenerationErrors = generationErrors,
            Multiplier = multiplier, LogPath = Path.GetFullPath(logPath), LogTimestamp = Timestamp(logPath), PartialLog = partial
        };

        void AddError(string message)
        {
            if (errors.Count == 100) errors.RemoveAt(0);
            errors.Add(message);
        }
    }

    // null marks a discarded oversized line. Never allocate an unbounded ReadLine string,
    // and never interpret a final line until its newline has actually been written.
    private static IEnumerable<string?> ReadCompleteLines(FileStream stream, long remaining, bool skipFirst)
    {
        var chunk = new byte[8192];
        var line = new byte[MaximumLineBytes];
        int used = 0;
        bool oversized = false;
        while (remaining > 0)
        {
            int read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, remaining));
            if (read == 0) yield break;
            remaining -= read;
            for (int index = 0; index < read; index++)
            {
                byte value = chunk[index];
                if (value == (byte)'\n')
                {
                    if (oversized) yield return null;
                    else if (!skipFirst)
                    {
                        int count = used > 0 && line[used - 1] == (byte)'\r' ? used - 1 : used;
                        yield return Encoding.UTF8.GetString(line, 0, count).TrimStart('\uFEFF');
                    }
                    skipFirst = oversized = false; used = 0;
                }
                else if (!skipFirst && !oversized)
                {
                    if (used < line.Length) line[used++] = value;
                    else { oversized = true; used = 0; }
                }
            }
        }
    }

    private static DateTimeOffset? Timestamp(string path)
    {
        var match = Regex.Match(Path.GetFileName(path), @"^nr_fg-(\d{8}-\d{6})(?:[-_]\d+)?\.log$", RegexOptions.CultureInvariant);
        try
        {
            return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var value) ? new DateTimeOffset(value) : null;
        }
        catch (ArgumentException) { return null; }
    }

    private static LogVerification Missing() => new(false, false, false, null, null, null, [], "log_missing");
}

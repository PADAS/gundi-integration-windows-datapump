namespace service;

using System.Text;

/// <summary>
/// Reads tail-of-file from the NLog output, used by the /logs page.
///
/// Designed to avoid loading the entire log into memory: we seek
/// backwards from the end, scanning for newlines, until we have N lines
/// or hit the start of the file. Cheap even when the log has grown to
/// hundreds of MB.
///
/// Filtering is applied after the read because NLog's level is embedded
/// inside each line as plain text (e.g. "info", "warn", "error"). Doing
/// it that way means we don't have to parse NLog's structured layout.
/// </summary>
public class LogService
{
    /// <summary>
    /// Resolves the log file path. NLog's default config writes to
    /// "radioservice.log" in the current working directory; Program.cs
    /// sets that to AppContext.BaseDirectory at startup, so the file
    /// sits next to the running exe regardless of how it was launched.
    /// </summary>
    public string LogFilePath =>
        Path.Combine(AppContext.BaseDirectory, "radioservice.log");

    /// <summary>True if the log file is currently present on disk.</summary>
    public bool LogFileExists => File.Exists(LogFilePath);

    /// <summary>
    /// Returns the last <paramref name="lineCount"/> lines from the log,
    /// optionally filtered to a minimum severity level. Returned in
    /// chronological order (oldest first), which is what the UI renders
    /// top-to-bottom.
    /// </summary>
    /// <param name="lineCount">Maximum number of lines to return.</param>
    /// <param name="minLevel">Minimum severity to include. <c>null</c> = all.</param>
    public IReadOnlyList<string> ReadTail(int lineCount, LogLevel? minLevel = null)
    {
        if (!LogFileExists) return Array.Empty<string>();

        // Read up to ~10x the requested line count from the tail, so
        // that filtering for "Errors only" still has enough material
        // to fill the page on a noisy log. Capped at 5 MB to keep
        // worst-case memory bounded.
        int targetLines = Math.Max(lineCount * 10, lineCount);
        var raw = ReadLastLines(LogFilePath, targetLines, maxBytes: 5 * 1024 * 1024);

        if (minLevel is null)
        {
            // Just trim to the requested tail length.
            return raw.Count > lineCount
                ? raw.GetRange(raw.Count - lineCount, lineCount)
                : raw;
        }

        // Filter and then trim to the requested tail length.
        var filtered = raw.Where(line => MatchesMinLevel(line, minLevel.Value)).ToList();
        return filtered.Count > lineCount
            ? filtered.GetRange(filtered.Count - lineCount, lineCount)
            : filtered;
    }

    /// <summary>
    /// Reads up to <paramref name="lineCount"/> lines from the end of
    /// the file, scanning backwards. Returns lines in chronological
    /// order. Tolerates concurrent writers by opening with
    /// FileShare.ReadWrite.
    /// </summary>
    private static List<string> ReadLastLines(string path, int lineCount, int maxBytes)
    {
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        long fileLen = fs.Length;
        long readFrom = Math.Max(0, fileLen - maxBytes);

        // Bias the start to a line boundary if we're not at file start.
        // Otherwise the first partial line we recover gets corrupted.
        if (readFrom > 0)
        {
            fs.Position = readFrom;
            int b;
            while ((b = fs.ReadByte()) != -1 && b != '\n') { /* seek past partial line */ }
            readFrom = fs.Position;
        }

        fs.Position = readFrom;
        // (long)fileLen - readFrom is bounded above by maxBytes (an int),
        // because readFrom = max(0, fileLen - maxBytes). So the cast is
        // safe -- but make it explicit and clamp to maxBytes anyway, both
        // for compile-time clarity (array lengths must be int) and to
        // guard against any future refactor that loosens the readFrom math.
        int bytesToRead = (int)Math.Min(maxBytes, fileLen - readFrom);
        var bytes = new byte[bytesToRead];
        int total = 0;
        while (total < bytes.Length)
        {
            int n = fs.Read(bytes, total, bytes.Length - total);
            if (n <= 0) break;
            total += n;
        }

        var text = Encoding.UTF8.GetString(bytes, 0, total);
        var lines = text.Split('\n');

        // Last "line" is often a trailing empty string from a final \n.
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];

        // Trim trailing \r on each (Windows line endings).
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r')) lines[i] = lines[i][..^1];
        }

        return lines.Length > lineCount
            ? lines[(lines.Length - lineCount)..].ToList()
            : lines.ToList();
    }

    private static bool MatchesMinLevel(string line, LogLevel min)
    {
        // Best-effort: NLog's default layout puts the level word ("info",
        // "warn", "error", "debug", "trace", "fatal") between the timestamp
        // and the message. Match by scanning for those tokens. Order
        // matters for "warn" vs "warning" and "error" vs "fatal".
        var lower = line.ToLowerInvariant();
        if (lower.Contains(" fatal ")) return min <= LogLevel.Fatal;
        if (lower.Contains(" error ")) return min <= LogLevel.Error;
        if (lower.Contains(" warn ") || lower.Contains(" warning ")) return min <= LogLevel.Warn;
        if (lower.Contains(" info ")) return min <= LogLevel.Info;
        if (lower.Contains(" debug ")) return min <= LogLevel.Debug;
        if (lower.Contains(" trace ")) return min <= LogLevel.Trace;

        // Couldn't classify — include in case it's a stack-trace continuation
        // from a previous error line.
        return true;
    }

    public enum LogLevel
    {
        Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4, Fatal = 5,
    }
}

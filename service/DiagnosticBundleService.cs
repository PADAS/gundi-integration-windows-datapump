namespace service;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Packages a "diagnostic bundle" zip the operator can download from the
/// Status page and email to support. Always sanitises secrets out of
/// appsettings.json before adding it to the zip; the rest of the contents
/// (log file, state.json, generated metadata) are non-sensitive.
/// </summary>
public class DiagnosticBundleService
{
    private readonly LogService _logSvc;
    private readonly PumpStatus _status;

    public DiagnosticBundleService(LogService logSvc, PumpStatus status)
    {
        _logSvc = logSvc;
        _status = status;
    }

    /// <summary>
    /// Filename to suggest to the browser. Includes the app version and a
    /// UTC timestamp so support can tell bundles apart at a glance and
    /// match them to a customer's reported time-of-issue.
    /// </summary>
    public string SuggestedFilename()
    {
        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        // Strip the +<sha> InformationalVersion suffix from the filename so
        // it doesn't end up with characters that some mail systems mangle.
        var safeVersion = _status.Version.Split('+')[0];
        return $"gundi-radio-diagnostics-{safeVersion}-{ts}.zip";
    }

    /// <summary>
    /// Writes the bundle to the supplied stream. Caller is responsible
    /// for managing the stream's lifetime; the bundle ZipArchive is
    /// constructed with leaveOpen=true so disposing it does not close
    /// the caller's stream.
    ///
    /// Streaming-friendly so the endpoint can pipe through a
    /// FileBufferingWriteStream rather than holding the entire zip
    /// (which can exceed 100 MB for a long-running install with a big
    /// log file) in process memory.
    /// </summary>
    public void BuildBundle(Stream target)
    {
        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        AddLogFile(zip);
        AddArchivedLogs(zip);
        AddRedactedAppSettings(zip);
        AddStateFile(zip);
        AddMetadata(zip);
    }

    private void AddLogFile(ZipArchive zip)
    {
        var path = _logSvc.LogFilePath;
        if (!File.Exists(path)) return;

        var entry = zip.CreateEntry("radioservice.log", CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        // FileShare.ReadWrite | Delete tolerates the running NLog writer.
        using var fileStream = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        fileStream.CopyTo(entryStream);
    }

    /// <summary>
    /// NLog's rolling-file target archives older logs to a "logs/"
    /// subdirectory at <c>${basedir}/logs/radioservice.${shortdate}.log</c>
    /// (see <c>NLog.config</c>). With archiveAboveSize=1 MB, current
    /// radioservice.log only holds the most recent ~1 MB of activity --
    /// the lines support actually needs are very often already in an
    /// archive. Pull them all into the bundle under "logs/".
    /// </summary>
    private static void AddArchivedLogs(ZipArchive zip)
    {
        var archiveDir = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(archiveDir)) return;

        foreach (var path in Directory.EnumerateFiles(archiveDir, "radioservice.*.log"))
        {
            var entryName = "logs/" + Path.GetFileName(path);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            fileStream.CopyTo(entryStream);
        }
    }

    private void AddRedactedAppSettings(ZipArchive zip)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return;

        var raw = File.ReadAllText(path);
        var redacted = RedactSecrets(raw);

        var entry = zip.CreateEntry("appsettings.json", CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(redacted);
    }

    private static void AddStateFile(ZipArchive zip)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "state.json");
        if (!File.Exists(path)) return;

        var entry = zip.CreateEntry("state.json", CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var fileStream = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        fileStream.CopyTo(entryStream);
    }

    private void AddMetadata(ZipArchive zip)
    {
        var entry = zip.CreateEntry("metadata.txt", CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(BuildMetadata());
    }

    private string BuildMetadata()
    {
        // Single atomic snapshot so the metadata's pump-status block is
        // internally consistent, even if a batch fires between fields.
        var s = _status.Snapshot();

        var sb = new StringBuilder();
        sb.AppendLine("Gundi Radio Service -- Diagnostic Bundle");
        sb.AppendLine();
        sb.AppendLine($"Generated:        {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"App version:      {s.Version}");
        sb.AppendLine();
        sb.AppendLine("--- Environment ---");
        sb.AppendLine($"Machine:          {Environment.MachineName}");
        sb.AppendLine($"OS:               {Environment.OSVersion}");
        sb.AppendLine($".NET runtime:     {Environment.Version}");
        sb.AppendLine($"Process bitness:  {(Environment.Is64BitProcess ? "x64" : "x86")}");
        sb.AppendLine($"Working dir:      {AppContext.BaseDirectory}");
        sb.AppendLine();
        sb.AppendLine("--- Pump status (live snapshot) ---");
        sb.AppendLine($"Started at:       {s.StartedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Running:          {s.IsRunning}");
        sb.AppendLine($"Paused:           {s.IsPaused}");
        sb.AppendLine($"Total batches:    {s.TotalBatches}");
        sb.AppendLine($"Total records:    {s.TotalRecords}");
        sb.AppendLine($"Last batch at:    {(s.LastBatchAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "(never)")}");
        sb.AppendLine($"Last batch count: {s.LastBatchCount}");
        sb.AppendLine($"Last cursor:      {s.LastCursor ?? "(none)"}");
        sb.AppendLine($"Last error:       {s.LastError ?? "(none)"}");
        sb.AppendLine();
        sb.AppendLine("--- Bundle contents ---");
        sb.AppendLine("radioservice.log   Current NLog output (most recent ~1 MB).");
        sb.AppendLine("logs/              Archived NLog rolls (older history).");
        sb.AppendLine("appsettings.json   Configuration. Passwords and API keys redacted.");
        sb.AppendLine("state.json         Cursor / high-water-mark used by the data pump.");
        sb.AppendLine("metadata.txt       This file.");
        return sb.ToString();
    }

    /// <summary>
    /// Recursively walks a JSON tree and replaces values for keys whose
    /// name suggests a secret. Conservative match: key contains
    /// "password", "apikey", "secret", or "token" (case-insensitive).
    /// </summary>
    internal static string RedactSecrets(string json)
    {
        try
        {
            // Be tolerant of hand-edited config: skip // comments and
            // trailing commas. The runtime IConfiguration binder is also
            // tolerant; matching its forgiveness here means we don't
            // suddenly bail on a file the running service was happy with.
            var docOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            var root = JsonNode.Parse(json, documentOptions: docOptions);
            if (root != null) RedactNode(root);
            return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                   ?? json;
        }
        catch
        {
            // If JSON is malformed, return a stub rather than risk shipping
            // raw (potentially secret-containing) text. Whoever opens the
            // bundle will see the explanation and know to ask the operator
            // separately.
            return "// appsettings.json could not be parsed for safe redaction\n" +
                   "// and was intentionally omitted from this bundle.\n";
        }
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            // Snapshot keys first because we mutate values during the walk.
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                var value = obj[key];
                if (IsSecretKey(key) && value is JsonValue && IsNonEmptyString(value))
                {
                    obj[key] = "[REDACTED]";
                }
                else if (value is JsonObject || value is JsonArray)
                {
                    RedactNode(value);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null) RedactNode(item);
            }
        }
    }

    private static bool IsNonEmptyString(JsonNode node)
    {
        try
        {
            var s = node.GetValue<string>();
            return !string.IsNullOrEmpty(s);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSecretKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("password")
            || lower.Contains("apikey")
            || lower.Contains("secret")
            || lower.Contains("token");
    }
}

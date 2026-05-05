namespace service;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Reads and writes appsettings.json. Preserves unrelated sections (Logging,
/// etc.) by parsing the file as a JsonNode tree, replacing only the
/// RouteConfiguration subtree, and writing back atomically.
///
/// Hot-reload: saves do NOT require a service restart. The Save handler in
/// the configuration UI calls into this service to persist, then signals
/// PumpController.RequestReload(). The pump's outer loop notices, disposes
/// the current reader (preserving cursor in state.json), and rebuilds with
/// the freshly-saved config.
///
/// JSON parsing is tolerant of comments and trailing commas to match what
/// IConfiguration accepts at runtime. A hand-edited file the running
/// service was happy with shouldn't suddenly be unsavable here.
/// </summary>
public class ConfigService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null, // preserve casing as-is
    };
    private static readonly JsonDocumentOptions JsonDocOpts = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ConfigService()
    {
        // appsettings.json sits next to the exe. Program.cs sets the working
        // directory to AppContext.BaseDirectory, so a relative path is fine
        // both when running interactively and when running as a service.
        _path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    /// <summary>
    /// Reads the current RouteConfiguration from disk. Returns a fresh
    /// default if the file is missing or the section isn't present.
    /// </summary>
    public RouteConfiguration LoadRouteConfig()
    {
        if (!File.Exists(_path)) return new RouteConfiguration();

        try
        {
            var json = File.ReadAllText(_path);
            var root = JsonNode.Parse(json, documentOptions: JsonDocOpts) as JsonObject;
            var section = root?["RouteConfiguration"];
            if (section is null) return new RouteConfiguration();
            return JsonSerializer.Deserialize<RouteConfiguration>(section.ToJsonString(), JsonOpts)
                   ?? new RouteConfiguration();
        }
        catch
        {
            // Better to surface a fresh default than crash the UI when the
            // file is malformed. The user can re-save through the form.
            return new RouteConfiguration();
        }
    }

    /// <summary>
    /// Writes RouteConfiguration back to appsettings.json, preserving any
    /// other top-level sections that may exist (Logging, etc.).
    /// </summary>
    public void SaveRouteConfig(RouteConfiguration config)
    {
        JsonObject root;
        if (File.Exists(_path))
        {
            var json = File.ReadAllText(_path);
            try
            {
                root = (JsonNode.Parse(json, documentOptions: JsonDocOpts) as JsonObject)
                       ?? new JsonObject();
            }
            catch
            {
                // Don't lose the user's save attempt because the file on
                // disk was malformed. Start a fresh root; the save will
                // produce a valid file (and any prior unrelated sections
                // are unfortunately lost — but they would also be
                // unrecoverable from malformed JSON anyway).
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        // Replace only the RouteConfiguration subtree.
        var serialized = JsonSerializer.SerializeToNode(config, JsonOpts);
        root["RouteConfiguration"] = serialized;

        // Atomic write: temp file + replace, so a crash mid-write never
        // leaves the operator with a truncated config file.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}

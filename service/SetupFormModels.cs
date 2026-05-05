namespace service;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Form-shaped projection of <see cref="RouteConfiguration"/>'s database
/// portion. Separate from the runtime model so we can put DataAnnotations
/// on it without polluting the runtime type, and so the password
/// "leave blank to keep" pattern is explicit.
///
/// Shared between the manual <c>Configuration</c> page and the first-run
/// <c>Setup</c> wizard so they round-trip the same shape.
/// </summary>
public class DbFormModel
{
    [Range(0, int.MaxValue, ErrorMessage = "Pick a reader type.")]
    public int ReaderTypeValue { get; set; } = -1;
    [Required] public string? Hostname { get; set; }
    [Required] public string? DatabaseName { get; set; }
    [Required] public string? Username { get; set; }
    public string? NewPassword { get; set; } = "";
    public string? DatabaseSchema { get; set; }
    [Range(1, 1000)] public int BatchSize { get; set; } = 25;
    public string? IntervalMs { get; set; } = "5000";
    [Range(1, 600)] public int ConnectionTimeoutSeconds { get; set; } = 30;
    [Range(1, 3600)] public int CommandTimeoutSeconds { get; set; } = 300;

    public static DbFormModel From(RouteConfiguration r) => new()
    {
        ReaderTypeValue = r.DatabaseType is null ? -1 : (int)r.DatabaseType.Type,
        Hostname = r.Hostname,
        DatabaseName = r.DatabaseName,
        Username = r.Username,
        DatabaseSchema = r.DatabaseSchema,
        BatchSize = r.BatchSize,
        IntervalMs = r.intervalMs,
        ConnectionTimeoutSeconds = r.ConnectionTimeoutSeconds,
        CommandTimeoutSeconds = r.CommandTimeoutSeconds,
    };

    public RouteConfiguration ToRouteConfig(string? existingPassword)
    {
        var supported = RadioDataPumpService.supportedReaders
            .FirstOrDefault(r => (int)r.Type == ReaderTypeValue);
        if (supported is null)
        {
            // Defensive: the dropdown is bound to supportedReaders so the UI
            // can't normally produce an unknown value, but a tampered request
            // body could. Throw a clear error here rather than null-forgiving
            // and persisting a config with DatabaseType = null. Callers catch
            // this and surface the message via the form's error display.
            throw new InvalidOperationException(
                $"Reader type {ReaderTypeValue} is not recognized. Pick a value from the dropdown.");
        }

        return new RouteConfiguration
        {
            DatabaseType = supported,
            Hostname = Hostname,
            DatabaseName = DatabaseName,
            Username = Username,
            Password = string.IsNullOrEmpty(NewPassword) ? existingPassword : NewPassword,
            DatabaseSchema = DatabaseSchema,
            BatchSize = BatchSize,
            intervalMs = IntervalMs,
            ConnectionTimeoutSeconds = ConnectionTimeoutSeconds,
            CommandTimeoutSeconds = CommandTimeoutSeconds,
        };
    }
}

/// <summary>
/// Form-shaped projection of a single <see cref="GundiConnection"/>.
/// Tracks the existing API key separately from what the user types, so
/// the "leave blank to keep" pattern works the same way as the DB
/// password.
/// </summary>
public class DestinationFormModel
{
    public string? ConnectionName { get; set; }
    public string? Destination { get; set; }
    public string? NewApiKey { get; set; } = "";
    public bool SendEverything { get; set; }
    public List<GroupAliasFormModel> GroupAliases { get; set; } = new();

    public string? ExistingApiKey { get; set; }
    public bool ApiKeySet => !string.IsNullOrEmpty(ExistingApiKey);

    // Transient UI state — not round-tripped through GundiConnection.
    public bool Testing { get; set; }
    public TestResult? LastTestResult { get; set; }

    public static DestinationFormModel From(GundiConnection c) => new()
    {
        ConnectionName = c.ConnectionName,
        Destination = c.Destination,
        SendEverything = c.SendEverything,
        ExistingApiKey = c.ApiKey,
        GroupAliases = (c.GroupAliases ?? new())
            .Select(GroupAliasFormModel.From).ToList(),
    };

    public GundiConnection ToConnection() => new()
    {
        ConnectionName = ConnectionName ?? "",
        Destination = Destination ?? "",
        ApiKey = string.IsNullOrEmpty(NewApiKey) ? (ExistingApiKey ?? "") : NewApiKey,
        SendEverything = SendEverything,
        GroupAliases = GroupAliases.Select(a => a.ToGroupAlias()).ToList(),
    };

    /// <summary>
    /// After a successful save, fold the just-typed key into "existing"
    /// so subsequent edits show "(unchanged)" placeholder.
    /// </summary>
    public void AcceptApiKey()
    {
        if (!string.IsNullOrEmpty(NewApiKey))
        {
            ExistingApiKey = NewApiKey;
            NewApiKey = "";
        }
    }
}

/// <summary>
/// Form-shaped projection of <see cref="GroupAlias"/>. The runtime model
/// marks both fields as <c>required</c>, which makes object-initializer
/// and blank-row construction awkward in the UI; this mirror uses plain
/// nullable strings and converts at save time.
/// </summary>
public class GroupAliasFormModel
{
    public string? Guid { get; set; }
    public string? Alias { get; set; }

    public static GroupAliasFormModel From(GroupAlias g) => new()
    {
        Guid = g.guid,
        Alias = g.alias,
    };

    public GroupAlias ToGroupAlias() => new()
    {
        guid = Guid ?? "",
        alias = Alias ?? "",
    };
}

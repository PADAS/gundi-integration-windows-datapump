using service;
using worker;

namespace service;

using NLog;
using DataPump;

using System.Threading.Tasks;

public class SupportedReader
{
    public string Name { get; set; }
    public RadioServiceConfiguration.ReaderType Type { get; set; }

    public override string ToString()
    {
        return Name;
    }
}


public class RouteConfiguration
{
    public string? Hostname { get; set; } = "localhost";
    public string? Username { get; set; } = "";
    public string? Password { get; set; } = "";
    public string? DatabaseName { get; set; } = "";

    // Nullable on purpose: the parameterless constructor leaves this
    // unset (a service with no configured DB type yet is a real state
    // -- the UI shows "Not configured. Open the web UI to set up the
    // service."). Consumers must null-check.
    public SupportedReader? DatabaseType { get; set; }

    public string? DatabaseSchema { get; set; } = "";
    public string? intervalMs { get; set; } = "5000";
    public int BatchSize { get; set; } = 25;

    // Per-command timeout in seconds (max time PG/SQL Server may take to
    // start returning rows for a single SELECT). Default 300 covers slow
    // first-run pulls against unindexed history tables; reduce if you
    // want failures to surface faster on a healthy DB.
    public int CommandTimeoutSeconds { get; set; } = 300;
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    // Nullable because JsonSerializer.Deserialize will overwrite the
    // ctor-initialized list with null if the JSON literally contains
    // "gundiConnections": null. Existing consumers already use the
    // `?? new()` pattern; declaring the property nullable makes the
    // type system match runtime reality.
    public List<GundiConnection>? gundiConnections { get; set; }

    public RouteConfiguration()
    {
        gundiConnections = new List<GundiConnection>();
    }

}

public class RadioDataPumpService : BackgroundService
{
    private readonly ILogger<RadioDataPumpService> _logger;
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly PumpStatus _status;
    private readonly ConfigService _configService;
    private readonly PumpController _controller;

    public static List<SupportedReader> supportedReaders = new List<SupportedReader>
            {
                new SupportedReader { Name = "Smart Dispatch Plus", Type = RadioServiceConfiguration.ReaderType.SmartDispatchPlus},
                new SupportedReader { Name = "Smart One Dispatch", Type = RadioServiceConfiguration.ReaderType.SmartOneDispatch },
                new SupportedReader { Name = "Kenwood KAS20", Type = RadioServiceConfiguration.ReaderType.KAS20 },
                new SupportedReader { Name = "TRBOnet", Type = RadioServiceConfiguration.ReaderType.TrbonetPlus }
            };

public RadioDataPumpService(ILogger<RadioDataPumpService> logger, PumpStatus status,
                                ConfigService configService, PumpController controller)
    {
        _logger = logger;
        _status = status;
        _configService = configService;
        _controller = controller;
    }

    /// <summary>
    /// Builds the appropriate IDataReader for a given route configuration.
    /// Single source of truth for the reader-type dispatch — used by both
    /// the running pump (ExecuteAsync) and the configuration UI's Test
    /// Connection button. Caller is responsible for disposing the result.
    /// </summary>
    public static IDataReader CreateReader(RouteConfiguration cfg)
    {
        if (cfg.DatabaseType is null)
            throw new InvalidOperationException("Reader type is not configured.");

        var ct  = cfg.ConnectionTimeoutSeconds;
        var cmt = cfg.CommandTimeoutSeconds;

        return cfg.DatabaseType.Type switch
        {
            RadioServiceConfiguration.ReaderType.KAS20 =>
                new KAS20DataReader(cfg.Hostname, cfg.DatabaseName, cfg.Username, cfg.Password, ct, cmt),

            RadioServiceConfiguration.ReaderType.TrbonetPlus =>
                new TrbonetPlusDataReader(cfg.Hostname, cfg.DatabaseName, cfg.Username, cfg.Password, ct, cmt),

            RadioServiceConfiguration.ReaderType.SmartDispatchPlus =>
                new SmartDispatchPlusV1Reader(cfg.Hostname, cfg.DatabaseName, cfg.Username, cfg.Password,
                                              cfg.DatabaseSchema, ct, cmt),

            RadioServiceConfiguration.ReaderType.SmartOneDispatch =>
                new SmartOneDispatchReader(cfg.Hostname, cfg.DatabaseName, cfg.Username, cfg.Password,
                                           cfg.DatabaseSchema, ct, cmt),

            _ => throw new InvalidOperationException(
                $"Unknown reader type: {cfg.DatabaseType.Type}")
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Outer loop: one iteration per configuration cycle.
        // - Load fresh config from disk (so saves via the UI take effect).
        // - Run the pump under a CTS linked to (stoppingToken | reloadToken).
        // - When reload fires, the inner pump cancels, the reader's Dispose
        //   flushes state.json (preserving cursor), and we loop back to
        //   re-read config and start a fresh pump.
        // - When stoppingToken fires (service shutdown), we exit the loop.
        while (!stoppingToken.IsCancellationRequested)
        {
            var routeConfig = _configService.LoadRouteConfig();

            if (routeConfig.DatabaseType == null)
            {
                logger.Warn("Database type not configured. Open {0} to set up the service.", ServiceManager.LocalUiUrl);
                _status.RecordError("Not configured. Open the web UI to set up the service.");

                // Idle until either the operator saves config (reload fires)
                // or the service is shutting down. Either way, loop back —
                // the while-condition re-checks stoppingToken on its own.
                using var idleLink = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, _controller.ReloadToken);
                try { await Task.Delay(Timeout.Infinite, idleLink.Token); }
                catch (OperationCanceledException) { }
                continue;
            }

            using var pumpLink = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, _controller.ReloadToken);
            var pumpToken = pumpLink.Token;

            IDataReader? reader = null;
            try
            {
                reader = CreateReader(routeConfig);

                var grouped_writer = new GroupedDataWriter();
                foreach (var gundiConnection in routeConfig.gundiConnections ?? new())
                {
                    var w = new GundiV2DataWriter(gundiConnection.Destination, gundiConnection.ApiKey);
                    // Honor SendEverything: when true, the destination wants
                    // every observation regardless of group. Adding any
                    // matching-group filter to GundiV2DataWriter causes it
                    // to drop records outside that group; an empty filter
                    // means pass-through. So skip the AddMatchingGroup loop
                    // entirely when SendEverything is on.
                    if (!gundiConnection.SendEverything)
                    {
                        foreach (var groupAlias in gundiConnection.GroupAliases ?? new())
                        {
                            w.AddMatchingGroup(groupAlias.guid);
                        }
                    }
                    grouped_writer.AddWriter(w);
                }

                IDataWriter data_writer = new StatusTrackingWriter(grouped_writer, _status);

                // intervalMs is stored as string for backwards compat.
                // Validate both that it parses AND that it lands in a
                // sensible range. Outside the range:
                //   0 or negative would make Task.Delay throw (or hang
                //     forever for -1).
                //   > 1 hour is almost certainly a typo (operator typed
                //     hours when they meant ms, etc.) and would cause the
                //     pump to look hung.
                // Either way, fall back to the default and surface the
                // bad value via the dashboard's last-error field.
                const int defaultIntervalMs = 5000;
                const int minIntervalMs = 100;          // 0.1s lower bound
                const int maxIntervalMs = 60 * 60_000;  // 1h upper bound

                int intervalMs = defaultIntervalMs;
                if (!string.IsNullOrEmpty(routeConfig.intervalMs))
                {
                    if (!int.TryParse(routeConfig.intervalMs, out var parsed))
                    {
                        logger.Warn("intervalMs '{0}' is not a number; falling back to {1}ms.",
                            routeConfig.intervalMs, defaultIntervalMs);
                        _status.RecordError(
                            $"intervalMs setting '{routeConfig.intervalMs}' is invalid; using {defaultIntervalMs}ms.");
                    }
                    else if (parsed < minIntervalMs || parsed > maxIntervalMs)
                    {
                        logger.Warn("intervalMs {0} is outside the safe range [{1}, {2}]; falling back to {3}ms.",
                            parsed, minIntervalMs, maxIntervalMs, defaultIntervalMs);
                        _status.RecordError(
                            $"intervalMs {parsed} is outside the safe range [{minIntervalMs}, {maxIntervalMs}]ms; using {defaultIntervalMs}ms.");
                    }
                    else
                    {
                        intervalMs = parsed;
                    }
                }

                var dataPump = new RadioDataPump(intervalMs, routeConfig.BatchSize);

                logger.Info("Starting pump.");
                _status.MarkRunning(true);
                try
                {
                    await dataPump.Run(reader, data_writer, pumpToken);
                }
                finally
                {
                    _status.MarkRunning(false);
                }
                logger.Info("Pump finished cleanly.");
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Reload requested. Reader will be disposed in finally; loop back.
                // (TaskCanceledException inherits from OperationCanceledException,
                //  so this single catch covers both shapes that .NET throws for
                //  cancellation — including the ones Polly's Task.Delay surfaces.)
                logger.Info("Configuration reload requested — restarting pump with new settings.");
            }
            catch (OperationCanceledException)
            {
                // Service shutdown (stoppingToken fired). Expected; exit cleanly.
                break;
            }
            catch (Exception ex)
            {
                // Non-transient error bubbled up here (transient ones are
                // retried inside RadioDataPump.Pump). These are nearly
                // always config problems the operator needs to fix —
                // missing schema, bad credentials, an unreachable Gundi
                // destination, etc. Crash-restarting via SCM doesn't help;
                // we'd just hit the same exception 60 seconds later in a
                // tight loop.
                //
                // Instead: log via both pipelines (so the failure is
                // visible in radioservice.log AND the Windows Application
                // Event Log / ASP.NET sinks), surface to the dashboard
                // via PumpStatus, and idle until the operator fixes
                // config in the web UI and triggers a reload via Save.
                //
                // Truly fatal failures (OOM, disk full, etc.) will kill
                // the process via the OS regardless of what we do here.
                _logger.LogError(ex, "{Message}", ex.Message);
                logger.Error(ex, "Unhandled exception in pump loop; service will idle until config is updated.");
                _status.RecordError(ex.Message);

                // Reader gets disposed by the outer finally; we dispose
                // it explicitly here too so subsequent loop iterations
                // (after reload) re-create it cleanly. Setting reader=null
                // also guards the finally from a double-dispose.
                reader?.Dispose();
                reader = null;

                using var idleLink = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, _controller.ReloadToken);
                try { await Task.Delay(Timeout.Infinite, idleLink.Token); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                // Reader's Dispose flushes state.json. Always run, even on
                // reload, so the cursor survives across config changes.
                reader?.Dispose();
            }
        }
    }
}

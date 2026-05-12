namespace service;

using NLog;

/// <summary>
/// Logs a periodic heartbeat to the NLog file *only when the pump is
/// quiet*. The pump's per-batch logs ("Posting batch of N observations")
/// already prove liveness whenever data is flowing, so a heartbeat during
/// active periods would just be noise. This service exists to give
/// operators a clear sign of life when nothing is happening.
///
/// Behaviour:
///   - Wakes every <see cref="_interval"/>.
///   - If the pump hasn't recorded a batch in that interval, logs a
///     one-line summary at info level. Otherwise stays silent.
///
/// The interval is operator-configurable via <c>Heartbeat:IntervalMinutes</c>
/// in <c>%PROGRAMDATA%\GundiRadioService\appsettings.json</c>. Default is
/// 60 minutes — high enough that the heartbeat lines don't drown out
/// useful entries in the log viewer, low enough to confirm the service
/// is still alive within a reasonable monitoring window. Clamped to a
/// 1-minute minimum so a misconfigured 0 doesn't turn the log into a
/// hot loop.
///
/// Replaces the prior "Tick." debug log that fired every pump cycle (~720
/// lines/hour at the default 5-second interval).
/// </summary>
public class HeartbeatService : BackgroundService
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Default heartbeat interval when the operator hasn't set one. Was
    /// 5 minutes in earlier versions; raised to 60 in 2026-05 after
    /// operators reported the log viewer being overwhelmed by heartbeat
    /// rows during long idle periods.
    /// </summary>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Floor for the configured interval. Set to 1 minute so a typo
    /// like <c>"IntervalMinutes": 0</c> doesn't degenerate into a busy
    /// loop emitting a line on every tick of the scheduler.
    /// </summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    private readonly TimeSpan _interval;
    private readonly PumpStatus _status;

    public HeartbeatService(IConfiguration config, PumpStatus status)
    {
        _status = status;
        _interval = ResolveInterval(config);
        logger.Info("HeartbeatService starting; interval = {0} minutes.",
            (int)_interval.TotalMinutes);
    }

    /// <summary>
    /// Reads <c>Heartbeat:IntervalMinutes</c> from configuration, falls
    /// back to <see cref="DefaultInterval"/> on a missing or unparseable
    /// value, and clamps to <see cref="MinInterval"/>. Lives as a static
    /// method so the constructor stays readable and the resolution rules
    /// are unit-testable in principle.
    /// </summary>
    private static TimeSpan ResolveInterval(IConfiguration config)
    {
        var raw = config["Heartbeat:IntervalMinutes"];
        if (string.IsNullOrWhiteSpace(raw)) return DefaultInterval;

        if (!int.TryParse(raw, out var minutes))
        {
            logger.Warn("Heartbeat:IntervalMinutes='{0}' is not an integer; " +
                        "falling back to {1} minutes.",
                        raw, (int)DefaultInterval.TotalMinutes);
            return DefaultInterval;
        }

        var requested = TimeSpan.FromMinutes(minutes);
        if (requested < MinInterval)
        {
            logger.Warn("Heartbeat:IntervalMinutes={0} is below the {1}-minute " +
                        "minimum; clamping to {1}.",
                        minutes, (int)MinInterval.TotalMinutes);
            return MinInterval;
        }
        return requested;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait one interval before the first check; immediately after
            // service startup is never a useful heartbeat, and the per-
            // batch logs from the actual pump usually fire within the
            // first few seconds anyway.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, stoppingToken);
                LogHeartbeatIfQuiet();
            }
        }
        catch (OperationCanceledException)
        {
            // Service shutdown. Expected.
        }
    }

    private void LogHeartbeatIfQuiet()
    {
        // Take a single atomic snapshot so the multi-field heartbeat
        // line can't observe a half-updated state (e.g. LastBatchAt
        // from after RecordBatch fires but TotalBatches still pre-
        // increment).
        var s = _status.Snapshot();

        // "Quiet" means we haven't recorded a batch in the interval.
        // If there's never been a batch, the time since service startup
        // stands in for "since last batch."
        var lastEvent = s.LastBatchAt ?? s.StartedAt;
        var idle = DateTime.UtcNow - lastEvent;
        if (idle < _interval) return;

        var minutes = (int)idle.TotalMinutes;
        var paused  = s.IsPaused ? " (paused)" : "";
        var cursor  = s.LastCursor ?? "(none)";
        var error   = s.LastError  ?? "(none)";

        logger.Info(
            "Heartbeat: idle for {0} minutes{1}; cursor at {2}; last error: {3}",
            minutes, paused, cursor, error);
    }
}

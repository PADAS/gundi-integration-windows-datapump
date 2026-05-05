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
///   - Wakes every <see cref="CheckInterval"/> (5 minutes by default).
///   - If the pump hasn't recorded a batch in the last
///     <see cref="IdleThreshold"/> (15 minutes by default), logs a one-line
///     summary at info level. Otherwise stays silent.
///
/// Replaces the prior "Tick." debug log that fired every pump cycle (~720
/// lines/hour at the default 5-second interval).
/// </summary>
public class HeartbeatService : BackgroundService
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(15);

    private readonly PumpStatus _status;

    public HeartbeatService(PumpStatus status)
    {
        _status = status;
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
                await Task.Delay(CheckInterval, stoppingToken);
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

        // "Quiet" means we haven't recorded a batch in IdleThreshold.
        // If there's never been a batch, the time since service startup
        // stands in for "since last batch."
        var lastEvent = s.LastBatchAt ?? s.StartedAt;
        var idle = DateTime.UtcNow - lastEvent;
        if (idle < IdleThreshold) return;

        var minutes = (int)idle.TotalMinutes;
        var paused  = s.IsPaused ? " (paused)" : "";
        var cursor  = s.LastCursor ?? "(none)";
        var error   = s.LastError  ?? "(none)";

        logger.Info(
            "Heartbeat: idle for {0} minutes{1}; cursor at {2}; last error: {3}",
            minutes, paused, cursor, error);
    }
}

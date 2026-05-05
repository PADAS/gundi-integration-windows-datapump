namespace service;

/// <summary>
/// Lifecycle controller for the data pump. Exposes a CancellationToken
/// that fires whenever the operator requests a reload (e.g. after saving
/// new configuration). The worker links its inner pump's cancellation to
/// this token, so a reload cancels in-flight work cleanly; the worker's
/// outer loop then re-reads config from disk and rebuilds the pump.
///
/// Singleton. Thread-safe — RequestReload may be called from a UI thread
/// while the worker is reading the token from another thread.
/// </summary>
public class PumpController
{
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Token that is cancelled when a reload is requested. The worker
    /// reads this once per outer-loop iteration; after a reload, the
    /// next read returns a fresh, uncancelled token.
    /// </summary>
    public CancellationToken ReloadToken => _cts.Token;

    /// <summary>
    /// Signal the running pump to stop and the worker to re-load config
    /// from disk. Atomic swap of the CTS so the worker's next read of
    /// ReloadToken returns the fresh, uncancelled token without the
    /// caller and the worker fighting over which one is "current".
    /// </summary>
    public void RequestReload()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}

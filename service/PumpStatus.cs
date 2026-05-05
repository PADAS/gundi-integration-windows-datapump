namespace service;

using System.Reflection;

/// <summary>
/// Shared, in-process state of the data pump. Updated by RadioDataPumpService
/// during its run loop, observed by the Blazor UI to render live status.
///
/// Singleton (one instance per service process). Thread-safe via a single
/// lock; the data shape is small and update frequency is low (per batch).
/// </summary>
public class PumpStatus
{
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public string? LastCursor { get; private set; }
    public DateTime? LastBatchAt { get; private set; }
    public int LastBatchCount { get; private set; }
    public long TotalBatches { get; private set; }
    public long TotalRecords { get; private set; }
    public string? LastError { get; private set; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public string Version =>
        typeof(PumpStatus).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

    /// <summary>Fires whenever any property changes. UI subscribes for live updates.</summary>
    public event Action? Changed;

    public void MarkRunning(bool running)
    {
        lock (_lock) IsRunning = running;
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        lock (_lock) IsPaused = !IsPaused;
        Changed?.Invoke();
    }

    public void RecordBatch(int count, string? cursor)
    {
        lock (_lock)
        {
            LastBatchAt = DateTime.UtcNow;
            LastBatchCount = count;
            TotalBatches++;
            TotalRecords += count;
            if (cursor is not null) LastCursor = cursor;
            LastError = null;
        }
        Changed?.Invoke();
    }

    public void RecordError(string message)
    {
        lock (_lock) LastError = message;
        Changed?.Invoke();
    }

    /// <summary>
    /// Operator-initiated dismissal of the last-error notice from the UI.
    /// LastError otherwise clears automatically on the next successful
    /// batch, but a configuration fix may take time to verify (no batches
    /// flowing yet) — leaving stale errors visible is misleading. The
    /// dashboard's "Dismiss" button calls into here.
    /// </summary>
    public void ClearError()
    {
        lock (_lock) LastError = null;
        Changed?.Invoke();
    }
}

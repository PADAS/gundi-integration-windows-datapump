namespace service;

using System.Reflection;

/// <summary>
/// Shared, in-process state of the data pump. Updated by RadioDataPumpService
/// during its run loop, observed by the Blazor UI to render live status.
///
/// Singleton (one instance per service process). Thread-safe — all reads
/// and writes go through a single lock. The lock is uncontested in
/// practice (batch updates happen at most a few times per second; UI
/// reads happen once per render), so the cost is negligible.
///
/// Why locking matters here: this app targets x86 (csproj Platforms
/// includes x86), where 64-bit reads/writes (DateTime, long) are not
/// guaranteed atomic. Reading <see cref="LastBatchAt"/>, <see cref="TotalBatches"/>,
/// or <see cref="TotalRecords"/> without the lock could produce a torn
/// value (32-bit halves from different writes).
///
/// Use <see cref="Snapshot"/> when you need multiple fields read
/// atomically together (e.g. heartbeat / diagnostic-bundle metadata).
/// Individual getter access is fine for the UI, where one render's
/// brief inconsistency between properties is harmless and the next
/// render fixes it.
/// </summary>
public class PumpStatus
{
    private readonly object _lock = new();

    private bool _isRunning;
    private bool _isPaused;
    private string? _lastCursor;
    private DateTime? _lastBatchAt;
    private int _lastBatchCount;
    private long _totalBatches;
    private long _totalRecords;
    private string? _lastError;

    public bool      IsRunning      { get { lock (_lock) return _isRunning;      } }
    public bool      IsPaused       { get { lock (_lock) return _isPaused;       } }
    public string?   LastCursor     { get { lock (_lock) return _lastCursor;     } }
    public DateTime? LastBatchAt    { get { lock (_lock) return _lastBatchAt;    } }
    public int       LastBatchCount { get { lock (_lock) return _lastBatchCount; } }
    public long      TotalBatches   { get { lock (_lock) return _totalBatches;   } }
    public long      TotalRecords   { get { lock (_lock) return _totalRecords;   } }
    public string?   LastError      { get { lock (_lock) return _lastError;      } }

    /// <summary>Set once at construction; safe to read without the lock.</summary>
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public string Version =>
        typeof(PumpStatus).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

    /// <summary>Fires whenever any property changes. UI subscribes for live updates.</summary>
    public event Action? Changed;

    /// <summary>
    /// Captures every field under the lock and returns an immutable record.
    /// Use this when you need a consistent multi-field view (heartbeat
    /// summary, diagnostic-bundle metadata, etc.). For single-field reads
    /// the property getters are equivalent and slightly cheaper.
    /// </summary>
    public PumpStatusSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new PumpStatusSnapshot(
                IsRunning:      _isRunning,
                IsPaused:       _isPaused,
                LastCursor:     _lastCursor,
                LastBatchAt:    _lastBatchAt,
                LastBatchCount: _lastBatchCount,
                TotalBatches:   _totalBatches,
                TotalRecords:   _totalRecords,
                LastError:      _lastError,
                StartedAt:      StartedAt,
                Version:        Version);
        }
    }

    public void MarkRunning(bool running)
    {
        lock (_lock) _isRunning = running;
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        lock (_lock) _isPaused = !_isPaused;
        Changed?.Invoke();
    }

    public void RecordBatch(int count, string? cursor)
    {
        lock (_lock)
        {
            _lastBatchAt = DateTime.UtcNow;
            _lastBatchCount = count;
            _totalBatches++;
            _totalRecords += count;
            if (cursor is not null) _lastCursor = cursor;
            _lastError = null;
        }
        Changed?.Invoke();
    }

    public void RecordError(string message)
    {
        lock (_lock) _lastError = message;
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
        lock (_lock) _lastError = null;
        Changed?.Invoke();
    }
}

/// <summary>
/// Atomic snapshot of <see cref="PumpStatus"/> for callers that need a
/// consistent multi-field view. Returned by <see cref="PumpStatus.Snapshot"/>.
/// </summary>
public record PumpStatusSnapshot(
    bool      IsRunning,
    bool      IsPaused,
    string?   LastCursor,
    DateTime? LastBatchAt,
    int       LastBatchCount,
    long      TotalBatches,
    long      TotalRecords,
    string?   LastError,
    DateTime  StartedAt,
    string    Version);

namespace service;

using DataPump;
using DataPumpModels;

/// <summary>
/// Decorator around an IDataWriter that updates PumpStatus on every batch
/// and stalls while PumpStatus.IsPaused is set. The pump itself never has
/// to know about either concern.
/// </summary>
public class StatusTrackingWriter : IDataWriter
{
    private readonly IDataWriter _inner;
    private readonly PumpStatus _status;

    public StatusTrackingWriter(IDataWriter inner, PumpStatus status)
    {
        _inner = inner;
        _status = status;
    }

    public async Task<int> PostObservation(ISourceRecord record, CancellationToken cancellation = default)
    {
        await WaitWhilePaused(cancellation);
        return await _inner.PostObservation(record, cancellation);
    }

    // Matches IDataWriter.PostObservations exactly. The previous signature
    // took List<ISourceRecord>, which is NOT the same as the interface's
    // IReadOnlyList<ISourceRecord> — so this method silently failed to
    // override the interface default and never ran. Symptoms: data flowed
    // (the default implementation called PostObservation per record), but
    // the dashboard never saw a batch update.
    public async Task<int> PostObservations(IReadOnlyList<ISourceRecord> records, CancellationToken cancellation = default)
    {
        await WaitWhilePaused(cancellation);
        try
        {
            var result = await _inner.PostObservations(records, cancellation);
            var maxCursor = records.Count > 0 ? records.Max(r => r.cursor_at) : (DateTime?)null;
            _status.RecordBatch(records.Count, maxCursor?.ToString("o"));
            return result;
        }
        catch (Exception ex)
        {
            _status.RecordError(ex.Message);
            throw;
        }
    }

    private async Task WaitWhilePaused(CancellationToken cancellation)
    {
        while (_status.IsPaused && !cancellation.IsCancellationRequested)
        {
            await Task.Delay(500, cancellation);
        }
    }
}

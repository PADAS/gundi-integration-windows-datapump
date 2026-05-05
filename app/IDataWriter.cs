using DataPumpModels;

public interface IDataWriter
{
    Task<int> PostObservation(ISourceRecord record, CancellationToken cancellation = default);

    /// <summary>
    /// Posts a batch of observations. Implementations should override this for efficient batch posting.
    /// Default implementation falls back to posting one at a time.
    /// </summary>
    Task<int> PostObservations(IReadOnlyList<ISourceRecord> records, CancellationToken cancellation = default)
    {
        return PostObservationsSequentially(records, cancellation);
    }

    /// <summary>
    /// Lightweight probe that verifies the writer can authenticate with its
    /// destination. Used by the configuration UI to validate API keys
    /// before saving. Default returns "not supported" — concrete writers
    /// (currently GundiV2DataWriter) override.
    /// </summary>
    Task<TestResult> TestConnection(CancellationToken cancellation = default)
    {
        return Task.FromResult(new TestResult(false, "Connection testing is not supported for this writer."));
    }

    /// <summary>
    /// Default sequential implementation for batch posting.
    /// </summary>
    protected async Task<int> PostObservationsSequentially(IReadOnlyList<ISourceRecord> records, CancellationToken cancellation)
    {
        foreach (var record in records)
        {
            await PostObservation(record, cancellation);
        }
        return 0;
    }
}

public class GundiConnection
{
    public string ConnectionName { get; set; }
    public string ApiKey { get; set; }
    public string Destination { get; set; }
    public bool SendEverything { get; set; }

    public List<GroupAlias> GroupAliases { get; set; }

    public GundiConnection()
    {
        GroupAliases = new List<GroupAlias>();
    }

}
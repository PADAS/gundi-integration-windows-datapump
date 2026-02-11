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
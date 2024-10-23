using DataPumpModels;

public interface IDataReader
{
    IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date);
}
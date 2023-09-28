using DataPumpModels;

public interface IDataReader
{
    IAsyncEnumerable<KenwoodGpsLogRecord> ReadNew(DateTime lower_date);
}
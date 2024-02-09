using DataPumpModels;

public interface IDataWriter
{
    Task<int> PostObservation(ISourceRecord record);
}
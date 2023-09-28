using DataPumpModels;

public interface IDataWriter
{
    Task<int> PostObservation(KenwoodGpsLogRecord record);
}
using DataPumpModels;

public interface IDataWriter
{
    Task<int> PostObservation(ISourceRecord record);
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
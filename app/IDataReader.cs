using DataPumpModels;

public class TestResult
{
    public TestResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }
    public Boolean Success { get; set; }
    public string Message { get; set; }
}

public interface IDataReader
{
    IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date);

    TestResult TestConnection();

    public List<GroupAlias> GetGroupAliases()
    {
        return new List<GroupAlias>();
    }
}

public class GroupAlias
{
    public required string guid { get; set; }
    public required string alias { get; set; }
}

public class GroupKeyPair
{
    public required string api_key { get; set; }
    public required string group_alias { get; set; }
    public required string group_guid { get; set; }
}

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
    public string guid { get; set; }
    public string alias { get; set; }
}

public class GroupKeyPair
{
    public string api_key { get; set; }
    public string group_alias { get; set; }
    public string group_guid { get; set; }
}


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
    public override string ToString()
    {
        return alias;
    }

    public override bool Equals(object obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }
        return guid == ((GroupAlias)obj).guid;
    }
}

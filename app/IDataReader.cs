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

public interface IDataReader : IDisposable
{
    IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date);

    /// <summary>
    /// The reader's resume position. Readers advance it as they yield
    /// records, before the pump has posted them. The pump snapshots it
    /// after each batch is handled, and restores the snapshot when a
    /// batch needs to be re-read (e.g. its post timed out).
    /// </summary>
    long GetCursor();

    void RestoreCursor(long cursor);

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

    public override int GetHashCode()
    {
        return guid.GetHashCode();
    }
}

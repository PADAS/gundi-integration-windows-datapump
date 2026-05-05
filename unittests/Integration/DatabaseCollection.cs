namespace DataPump.Tests.Integration;

[CollectionDefinition("Database", DisableParallelization = true)]
public class DatabaseCollection { }

public abstract class ReaderTestsBase : IDisposable
{
    protected ReaderTestsBase() => ClearStateFile();
    public void Dispose() => ClearStateFile();

    static void ClearStateFile()
    {
        if (File.Exists("state.json")) File.Delete("state.json");
    }
}

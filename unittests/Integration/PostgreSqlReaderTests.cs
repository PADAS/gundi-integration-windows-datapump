using Xunit;

namespace DataPump.Tests.Integration;

[Collection("Database")]
public class SmartDispatchPlusV1ReaderTests : ReaderTestsBase
{
    const string Prefix = "SMARTDISPATCH";
    const string SkipReason = "Set GUNDI_TEST_SMARTDISPATCH_CONNSTR (and optionally _SCHEMA) to enable.";

    [SkippableFact]
    public void TestConnection_Succeeds()
    {
        var cfg = PgTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new SmartDispatchPlusV1Reader(cfg!.Host, cfg.Database, cfg.Username, cfg.Password, cfg.Schema);
        var result = reader.TestConnection();

        Assert.True(result.Success, result.Message);
    }

    [SkippableFact]
    public async Task ReadNew_ExecutesQueryAndMapsRows()
    {
        var cfg = PgTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new SmartDispatchPlusV1Reader(cfg!.Host, cfg.Database, cfg.Username, cfg.Password, cfg.Schema);
        var since = DateTime.UtcNow.AddYears(-20);

        int count = 0;
        await foreach (var record in reader.ReadNew(since))
        {
            Assert.NotNull(record);
            if (++count >= 5) break;
        }
    }

    [SkippableFact]
    public void GetGroupAliases_ExecutesWithoutError()
    {
        var cfg = PgTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new SmartDispatchPlusV1Reader(cfg!.Host, cfg.Database, cfg.Username, cfg.Password, cfg.Schema);
        var groups = reader.GetGroupAliases();

        Assert.NotNull(groups);
    }
}

[Collection("Database")]
public class SmartOneDispatchReaderTests : ReaderTestsBase
{
    const string Prefix = "SMARTONE";
    const string SkipReason = "Set GUNDI_TEST_SMARTONE_CONNSTR (and optionally _SCHEMA) to enable.";

    [SkippableFact]
    public void TestConnection_Succeeds()
    {
        var cfg = PgTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new SmartOneDispatchReader(cfg!.Host, cfg.Database, cfg.Username, cfg.Password, cfg.Schema);
        var result = reader.TestConnection();

        Assert.True(result.Success, result.Message);
    }

    [SkippableFact]
    public async Task ReadNew_ExecutesQueryAndMapsRows()
    {
        var cfg = PgTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new SmartOneDispatchReader(cfg!.Host, cfg.Database, cfg.Username, cfg.Password, cfg.Schema);
        var since = DateTime.UtcNow.AddYears(-20);

        int count = 0;
        await foreach (var record in reader.ReadNew(since))
        {
            Assert.NotNull(record);
            if (++count >= 5) break;
        }
    }
}

using Xunit;

namespace DataPump.Tests.Integration;

[Collection("Database")]
public class KAS20DataReaderTests : ReaderTestsBase
{
    const string Prefix = "KAS20";
    const string SkipReason = "Set GUNDI_TEST_KAS20_CONNSTR to enable.";

    [SkippableFact]
    public void TestConnection_Succeeds()
    {
        var cfg = MsSqlTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new KAS20DataReader(cfg!.Server, cfg.Database, cfg.Username, cfg.Password);
        var result = reader.TestConnection();

        Assert.True(result.Success, result.Message);
    }

    [SkippableFact]
    public async Task ReadNew_ExecutesQueryAndMapsRows()
    {
        var cfg = MsSqlTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new KAS20DataReader(cfg!.Server, cfg.Database, cfg.Username, cfg.Password);
        var since = DateTime.UtcNow.AddYears(-20);

        int count = 0;
        await foreach (var record in reader.ReadNew(since))
        {
            Assert.NotNull(record);
            if (++count >= 5) break;
        }
    }
}

[Collection("Database")]
public class TrbonetPlusDataReaderTests : ReaderTestsBase
{
    const string Prefix = "TRBONET";
    const string SkipReason = "Set GUNDI_TEST_TRBONET_CONNSTR to enable.";

    [SkippableFact]
    public void TestConnection_Succeeds()
    {
        var cfg = MsSqlTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new TrbonetPlusDataReader(cfg!.Server, cfg.Database, cfg.Username, cfg.Password);
        var result = reader.TestConnection();

        Assert.True(result.Success, result.Message);
    }

    [SkippableFact]
    public async Task ReadNew_ExecutesQueryAndMapsRows()
    {
        var cfg = MsSqlTestConfig.FromEnv(Prefix);
        Skip.If(cfg is null, SkipReason);

        var reader = new TrbonetPlusDataReader(cfg!.Server, cfg.Database, cfg.Username, cfg.Password);
        var since = DateTime.UtcNow.AddYears(-20);

        int count = 0;
        await foreach (var record in reader.ReadNew(since))
        {
            Assert.NotNull(record);
            if (++count >= 5) break;
        }
    }
}

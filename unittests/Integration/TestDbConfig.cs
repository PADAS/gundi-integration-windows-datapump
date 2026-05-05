using Microsoft.Data.SqlClient;
using Npgsql;

namespace DataPump.Tests.Integration;

internal record PgTestConfig(string Host, string Database, string Username, string Password, string Schema)
{
    public static PgTestConfig? FromEnv(string prefix)
    {
        var connStr = Environment.GetEnvironmentVariable($"GUNDI_TEST_{prefix}_CONNSTR");
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        var b = new NpgsqlConnectionStringBuilder(connStr);
        var schema = Environment.GetEnvironmentVariable($"GUNDI_TEST_{prefix}_SCHEMA") ?? "public";

        return new PgTestConfig(
            Host: b.Host ?? "localhost",
            Database: b.Database ?? "",
            Username: b.Username ?? "",
            Password: b.Password ?? "",
            Schema: schema);
    }
}

internal record MsSqlTestConfig(string Server, string Database, string Username, string Password)
{
    public static MsSqlTestConfig? FromEnv(string prefix)
    {
        var connStr = Environment.GetEnvironmentVariable($"GUNDI_TEST_{prefix}_CONNSTR");
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        var b = new SqlConnectionStringBuilder(connStr);

        return new MsSqlTestConfig(
            Server: b.DataSource ?? "localhost",
            Database: b.InitialCatalog ?? "",
            Username: b.UserID ?? "",
            Password: b.Password ?? "");
    }
}

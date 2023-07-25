using System.Text.Json;
using Models;
using Npgsql;
using System.Configuration;
using NLog;

internal class Program
{

    private static Logger logger = LogManager.GetCurrentClassLogger();

    private static async Task<int> Main(string[] args)
    {

        var logconfig = LogManager.Configuration ?? new NLog.Config.LoggingConfiguration();

        logger.Info("Data pump service started.");
        logger.Info("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:sszzz"));

        string? destination = ConfigurationManager.AppSettings.Get("destination");
        string? intervalMs = ConfigurationManager.AppSettings["intervalMs"];
        string? connectString = ConfigurationManager.AppSettings["connectString"];


        if (destination == null)
        {
            logger.Info("destination is null. Stubbornly refusing to run.");
            return 0;
        }

        if (connectString == null)
        {
            logger.Info("connectString is null. Stubbornly refusing to run.");
            return 0;
        }



        var dataPump = new DataPump(intervalMs == null ? 5000 : int.Parse(intervalMs));

        var val = await dataPump.Run(new DataReader(connectString = connectString), new DataWriter(destination = destination));

        logger.Info("Data pump service finished.");
        logger.Info("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:sszzz"));
        return val;

    }

}


class DataPump
{

    int _intervalMs = 5000;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public DataPump(int intervalMs = 5000)
    {
        _intervalMs = intervalMs;

    }
    public async Task<int> Run(DataReader reader, DataWriter writer)
    {

        var val = 0;
        while (val <= 1)
        {
            val = await this.Pump(reader, writer);
            logger.Info("val: " + val.ToString());
            await Task.Delay(this._intervalMs).ContinueWith(_ => logger.Info("World!"));

        }
        return 1;

    }

    private async Task<int> Pump(DataReader reader, DataWriter writer)
    {
        var l = await reader.ReadNew();
        logger.Info("l: " + l.ToString());
        // await Task.Delay(5000).ContinueWith(_ => logger.Info("World!"));
        var t = await writer.FetchDestinationStatus();

        logger.Info("val2: " + JsonSerializer.Serialize(t));
        return l;
    }
}
class DataReader
{

    private string _connectionString;
    private int counter = 0;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public DataReader(string connectString)
    {

        _connectionString = connectString;
    }

    public async Task<int> ReadNew()
    {

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(this._connectionString);
        var dataSource = dataSourceBuilder.Build();

        var conn = await dataSource.OpenConnectionAsync();

        // // Insert some data
        // await using (var cmd = new NpgsqlCommand("INSERT INTO data (some_field) VALUES (@p)", conn))
        // {
        //     cmd.Parameters.AddWithValue("p", "Hello world");
        //     await cmd.ExecuteNonQueryAsync();
        // }

        // Retrieve all rows
        await using (var cmd = new NpgsqlCommand("SELECT username, email, first_name, last_name  FROM auth_user", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())

                logger.Info($"username: {reader.GetString(0)}, email: {reader.GetString(1)}, first_name: {reader.GetString(2)}, last_name: {reader.GetString(3)}");
        }
        return counter++;
    }
}
class DataWriter
{
    private readonly HttpClient _httpClient;
    private readonly string _destination;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public DataWriter(string destination = "https://develop.pamdas.org")
    {
        this._httpClient = new HttpClient();
        this._destination = destination;

    }

    public async Task<StatusResponse> FetchDestinationStatus()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{this._destination}/api/v1.0/status");
            var content = await response.Content.ReadAsStringAsync();

            StatusResponse? status_response = JsonSerializer.Deserialize<StatusResponse>(content);
            return status_response ?? new StatusResponse();

        }
        catch (Exception e)
        {
            logger.Info("Exception: " + e.Message);
            return new StatusResponse();
        }
    }
}
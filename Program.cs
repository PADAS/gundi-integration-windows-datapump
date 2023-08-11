using System.Text.Json;
using Models;
using Npgsql;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using NLog;
using Microsoft.Data.SqlClient;
using System.Net.Http.Json;
using System.Net.Http.Headers;

using System.Threading.Tasks;


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
        //string? earthranger_url = ConfigurationManager.AppSettings["earthranger_url"];
        //string? earthranger_username = ConfigurationManager.AppSettings["earthranger_username"];
        //string? earthranger_password = ConfigurationManager.AppSettings["earthranger_password"];
        //string? earthranger_client_id = ConfigurationManager.AppSettings["earthranger_client_id"];
        string? earthranger_provider_key = ConfigurationManager.AppSettings["earthranger_provider_key"];
        string? earthranger_auth_token = ConfigurationManager.AppSettings["earthranger_auth_token"];


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

        var val = await dataPump.Run(
            new DataReader(connectString), 
            new DataWriter(destination, earthranger_auth_token, earthranger_provider_key));

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

         
        var val = await this.Pump(reader, writer);
        logger.Info("val: " + val.ToString());
        await Task.Delay(this._intervalMs).ContinueWith(_ => logger.Info("World!"));

        return 1;

    }

    private async Task<int> Pump(DataReader reader, DataWriter writer)
    {
        var lower_date = DateTime.UtcNow.AddMinutes(-10);
        while (true)
        {
            await foreach (var item in reader.ReadNew(lower_date))
            {
                logger.Info("item: " + JsonSerializer.Serialize(item));

                await writer.PostObservation(item);

                lower_date = item.updated_at > lower_date ? item.updated_at : lower_date;
            }

            await Task.Delay(this._intervalMs).ContinueWith(_ => logger.Info("Continuing!"));
        }
        //logger.Info("l: " + l.ToString());
        // await Task.Delay(5000).ContinueWith(_ => logger.Info("World!"));
        var t = await writer.FetchDestinationStatus();


        logger.Info("val2: " + JsonSerializer.Serialize(t));
        return 0;
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


    private static decimal to_decimal_degrees(double value)
    {
        /*
         * This logic is based on conjecture. Looking atsamples in a live KAS-20 database, comparing stored
         * values to what's reflected in the Dispatch user interface. The Dispatch UI shows values to 5 decimal.
         * The stored value is effectively (degrees * 100) + (decimal minutes). This function converts to
         * decimal degrees.
         */

        int value_degrees = (int)(value / 100);

        double value_fraction = value - (value_degrees * 100);

        double value_decimal = value_degrees + value_fraction / 60;

        return decimal.Round((decimal)value_decimal, 5);
    }

    public async IAsyncEnumerable<KenwoodGpsLogRecord> ReadNew(DateTime lower_date)
    {

        SqlConnection connection = new SqlConnection(this._connectionString);
        var query = "select top 1000 system_id, date_time, latitude, longitude, e_w, n_s," +
            " speed, unit_id, global_id, name, created_date, updated_date" +
            " from GpsLog" +
            " where updated_date > @lowwer_date" +
            " order by date_time desc;";
        connection.Open();

        using (SqlCommand command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@lowwer_date", lower_date);
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    int systemId = reader.GetInt32(reader.GetOrdinal("system_id"));
                    DateTime dateTime = reader.GetDateTime(reader.GetOrdinal("date_time"));
                    dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                    string e_w = reader.GetString(reader.GetOrdinal("e_w"));
                    string n_s = reader.GetString(reader.GetOrdinal("n_s"));
                    string name = reader.GetString(reader.GetOrdinal("name"));
                    long unitId = reader.GetInt64(reader.GetOrdinal("unit_id"));
                    DateTime createdDate = reader.GetDateTime(reader.GetOrdinal("created_date"));
                    DateTime updatedDate = reader.GetDateTime(reader.GetOrdinal("updated_date"));


                    double stored_latitude = reader.GetDouble(reader.GetOrdinal("latitude"));
                    double stored_longitude = reader.GetDouble(reader.GetOrdinal("longitude"));
                    decimal latitude = to_decimal_degrees(stored_latitude);
                    decimal longitude = to_decimal_degrees(stored_longitude);
                    latitude = n_s.Equals("S", StringComparison.OrdinalIgnoreCase) ? latitude * -1 : latitude;
                    longitude = e_w.Equals("W", StringComparison.OrdinalIgnoreCase) ? longitude * -1 : longitude;


                    var item = new KenwoodGpsLogRecord();

                    item.name = name;
                    item.unit_id = unitId;
                    item.e_w = e_w;
                    item.n_s = n_s;
                    item.created_at = createdDate;
                    item.updated_at = updatedDate;
                    item.recorded_at = dateTime;
                    item.latitude = latitude;
                    item.longitude = longitude;
                    item.system_id = systemId;

                    //Console.WriteLine(JsonSerializer.Serialize<KenwoodGpsLogRecord>(item));
                    yield return item;  
                }
            }
        }

        connection.Close();
 
    }
}
class DataWriter
{
    private readonly HttpClient _httpClient;
    private readonly string _destination;
    private readonly string _token;
    private readonly string _provider_key;  

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public DataWriter(string destination = "https://cdip-er.pamdas.org", string token = "SomethingFancy", string provider_key = "kenwood-radios")
    {
        this._httpClient = new HttpClient();
        this._destination = destination;
        this._token = token;
        this._provider_key = provider_key;

        this._httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this._token);
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

    public async Task<int> PostObservation(KenwoodGpsLogRecord record)
    {
        try
        {
            var observation = new EarthRangerObservation();
            var location = new EarthRangerLocation();
            location.lat = record.latitude;
            location.lon = record.longitude;
            observation.location = location;

            observation.subject_name = record.name; 
            observation.manufacturer_id = record.unit_id.ToString();    
            observation.recorded_at = record.recorded_at;   

            observation.additional = new Dictionary<string, object>();

            logger.Info(JsonSerializer.Serialize<EarthRangerObservation>(observation));
            var response = await _httpClient.PostAsJsonAsync<EarthRangerObservation>($"{this._destination}/api/v1.0/sensors/dasradioagent/{this._provider_key}/status", observation);
            var content = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            //StatusResponse? status_response = JsonSerializer.Deserialize<StatusResponse>(content);
            //return status_response?.status ?? 0;
            return 0;
        }
        catch (HttpRequestException e)
        {
            logger.Warn("Exception: " + e.Message); 
        }
        catch (Exception e)
        {
            logger.Info("Exception: " + e.Message);
        }

        return 0;   
    }
}
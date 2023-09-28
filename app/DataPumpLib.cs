using System.Text.Json;
using DataPumpModels;
using System.Configuration;
using NLog;
using Microsoft.Data.SqlClient;
using System.Net.Http.Json;
using System.Net.Http.Headers;



public class KAS20DataReader : IDataReader
{

    private string _connectionString;
    private int _kas20_system_id;
    private int counter = 0;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public KAS20DataReader(string connectionString, int kas20_system_id)
    {

        _connectionString = connectionString;
        _kas20_system_id = kas20_system_id;
    }


    private static decimal to_decimal_degrees(double value)
    {
        /*
         * This logic is based on conjecture. Looking at samples in a live KAS-20 database, comparing stored
         * values to what's reflected in the Dispatch user interface. The Dispatch UI shows values to 5 decimal.
         * The stored value is effectively (degrees * 100) + (decimal minutes). This function converts to
         * decimal degrees.
         */

        int value_degrees = (int)(value / 100);

        double value_fraction = value - (value_degrees * 100);

        double value_decimal = value_degrees + value_fraction / 60;

        return decimal.Round((decimal)value_decimal, 5);
    }

   async  public IAsyncEnumerable<KenwoodGpsLogRecord> ReadNew(DateTime lower_date)
    {
        using DataPump.StateHandler state_handler = new DataPump.StateHandler("state.json");
        DataPump.State state = state_handler.LoadState();


        SqlConnection connection = new SqlConnection(this._connectionString);
        var query = "select top 1000 gps_index, system_id, date_time, latitude, longitude, e_w, n_s," +
            " speed, unit_id, global_id, name, created_date, updated_date" +
            " from GpsLog" +
            " where updated_date > @lowwer_date" +
            " and system_id = @kas20_system_id" +
            " and gps_index > @latest_gps_index" +
            " order by date_time asc;";
    
        connection.Open();


        using (SqlCommand command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@lowwer_date", lower_date);
            command.Parameters.AddWithValue("@kas20_system_id", _kas20_system_id);
            command.Parameters.AddWithValue("@latest_gps_index", state.latest_gps_index);

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
                    long gps_index = reader.GetInt64(reader.GetOrdinal("gps_index"));


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
                    state.latest_gps_index = gps_index;
                }
            }

           
        }

        connection.Close();

    }
}

public class GundiDataWriter : IDataWriter
{
    private readonly HttpClient _httpClient;
    private readonly string _destination;
    private readonly string _apikey;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public GundiDataWriter(string destination = "https://cdip-api.pamdas.org", string apikey = "SomethingFancy")
    {
        this._httpClient = new HttpClient();
        this._destination = destination;
        this._apikey = apikey;
        this._httpClient.DefaultRequestHeaders.Add("apikey", this._apikey);
    }


    public async Task<int> PostObservation(KenwoodGpsLogRecord record)
    {
        try
        {
            var location = new GundiLocation();
            location.y = record.latitude;
            location.x = record.longitude;

            var position = new GundiPosition();
            position.location = location;

            position.name = record.name;
            position.device_id = record.unit_id.ToString();
            position.recorded_at = record.recorded_at;
            position.type = "gps-radio";

            position.additional = new Dictionary<string, object>()
            {
                { "system_id", record.system_id },
                {"e_w", record.e_w },
                { "n_s", record.n_s },
                {"created_at", record.created_at },
                {"updated_at", record.updated_at},
                {"unit_id", record.unit_id }

            };
            List<GundiPosition> payload = new() { position};

            logger.Info(JsonSerializer.Serialize<GundiPosition>(position));
            var response = await _httpClient.PostAsJsonAsync<List<GundiPosition>>($"{this._destination}/positions/", payload);
            var content = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

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
public class EarthRangerDataWriter : IDataWriter
{
    private readonly HttpClient _httpClient;
    private readonly string _destination;
    private readonly string _token;
    private readonly string _provider_key;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public EarthRangerDataWriter(string destination = "https://cdip-er.pamdas.org", string token = "SomethingFancy", string provider_key = "kenwood-radios")
    {
        this._httpClient = new HttpClient();
        this._destination = destination;
        this._token = token;
        this._provider_key = provider_key;

        this._httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this._token);
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

            observation.additional = new Dictionary<string, object> ()
            {
                { "system_id", record.system_id },
                { "e_w", record.e_w },
                { "n_s", record.n_s },
                { "created_at", record.created_at },
                { "updated_at", record.updated_at},
                { "unit_id", record.unit_id }

            };

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
using System.Text.Json;
using DataPumpModels;

using System.Configuration;
using NLog;
using Microsoft.Data.SqlClient;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Data.SqlTypes;

using Npgsql;
using System;
using System.ComponentModel;
using Microsoft.IdentityModel.Tokens;

public class KAS20DataReader : IDataReader
{

    private string _connectionString;
    private int counter = 0;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public KAS20DataReader(string database_server, string database_name, string database_user, string database_password)
    {

        _connectionString = $"Data Source={database_server};User ID={database_user};Password={database_password};Initial Catalog={database_name};TrustServerCertificate=True;";
    
    }

    public TestResult TestConnection()
    {
        try
        {


            SqlConnection connection = new SqlConnection(this._connectionString);
            var query = "select top 1 * from GpsLog;";
            connection.Open();
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new TestResult(true, "All good.");
                    }
                }
            }
        }
        catch (SqlException e)
        {
            return new TestResult(false, e.Message);
        }
        catch (Exception e)
        {
            logger.Warn("Exception: " + e.Message);
        }   

        return new TestResult(false, "Something went wrong.");
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

   async  public IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date)
    {
        using DataPump.StateHandler state_handler = new DataPump.StateHandler("state.json");
        DataPump.State state = state_handler.LoadState();


        SqlConnection connection = new SqlConnection(this._connectionString);
        var query = "select top 1000 gps_index, system_id, date_time, latitude, longitude, e_w, n_s," +
            " speed, unit_id, global_id, name, created_date, updated_date" +
            " from GpsLog" +
            " where date_time > @lower_date" +
            " and gps_index > @latest_gps_index" +
            " order by date_time asc;";
    
        connection.Open();


        using (SqlCommand command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@lower_date", lower_date);
            command.Parameters.AddWithValue("@latest_gps_index", state.latest_gps_index);

            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    KenwoodGpsLogRecord item = null;
                    try
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

                        item = new KenwoodGpsLogRecord();

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

                        // Advance cursor in state.
                        state.latest_gps_index = gps_index;

                    }
                    catch (SqlNullValueException e)
                    {
                        logger.Warn("Null value found in GpsLog record. exception: " + e.Message);
                    }

                    if (item != null)
                    {
                        yield return item;
                    }


                }
            }

           
        }

        connection.Close();

    }
}

public class TrbonetPlusDataReader : IDataReader
{

    private string _connectionString;
    private int counter = 0;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public TrbonetPlusDataReader(string database_server, string database_name, string database_user, string database_password)
    {

        _connectionString = $"Data Source={database_server};User ID={database_user};Password={database_password};Initial Catalog={database_name};TrustServerCertificate=True;";

    }

    public TestResult TestConnection()
    {
       try
        {
            SqlConnection connection = new SqlConnection(this._connectionString);
            var query = "SELECT TOP (1) device_id FROM [GpsInfo] order by id desc;";
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new TestResult(true, "All good.");
                    }
                }   
            }
        }
        catch (Exception e)
        {
            return new TestResult(false, e.Message);
        }
        return new TestResult(false, "Something went wrong.");
    }



    async public IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date)
    {
        using DataPump.StateHandler state_handler = new DataPump.StateHandler("state.json");
        DataPump.State state = state_handler.LoadState();


        SqlConnection connection = new SqlConnection(this._connectionString);
        var query = "SELECT TOP (1000) g.id " +
                    ", g.device_id " +
	                  " ,d.name " +
                      " ,g.date " +
                      " ,g.dateUtc " +
                      " ,g.active " +
                      " ,g.longitude " +
                      " ,g.latitude " +
                      " ,g.altitude " +
                      " ,g.radius " +
                      " ,g.direction " +
                      " ,g.speed " +
                      " ,g.description " +
                      " ,g.rssi " + 
                      " ,g.reportId " +
                      " ,g.gpsSource " +
                      " FROM [GpsInfo] g " +
                      " JOIN [Devices] d " +
                      "     on g.device_id = d.id " + 
                      " WHERE g.dateUtc > @lower_date" +
                      " and g.id > @latest_gps_index" +
                      " and g.dateUtc is not null " +
                      " order by id asc; ";

        connection.Open();


        using (SqlCommand command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@lower_date", lower_date);
            command.Parameters.AddWithValue("@latest_gps_index", state.latest_gps_index);

            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    TrbonetPlusRecord item = null;
                    try
                    {
                        int id = reader.GetInt32(reader.GetOrdinal("id"));
                        DateTime dateUtc = reader.GetDateTime(reader.GetOrdinal("dateUtc"));
                        DateTime recorded_at = DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc);
                        string name = reader.GetString(reader.GetOrdinal("name"));
                        int device_id = reader.GetInt32(reader.GetOrdinal("device_id"));

                        double stored_latitude = reader.GetDouble(reader.GetOrdinal("latitude"));
                        double stored_longitude = reader.GetDouble(reader.GetOrdinal("longitude"));

                        decimal latitude = decimal.Round((decimal)stored_latitude, 5);
                        decimal longitude = decimal.Round((decimal)stored_longitude, 5);
                        
                        double altitude = reader.GetDouble(reader.GetOrdinal("altitude"));
                        double radius = reader.GetDouble(reader.GetOrdinal("radius"));
                        int direction = reader.GetInt32(reader.GetOrdinal("direction"));
                        double speed = reader.GetDouble(reader.GetOrdinal("speed"));
                        double rssi = reader.GetDouble(reader.GetOrdinal("rssi"));
                        string description = reader.GetString(reader.GetOrdinal("description"));
                        int reportId = reader.GetInt32(reader.GetOrdinal("reportId"));
                        int gpsSource = reader.GetByte(reader.GetOrdinal("gpsSource"));
                        item = new TrbonetPlusRecord();

                        item.name = name;
                        item.device_id = device_id;
                        item.recorded_at = recorded_at;
                        item.latitude = latitude;
                        item.longitude = longitude;
                        item.gpsSource = gpsSource;
                        item.reportId = reportId;
                        item.altitude = altitude;
                        item.speed = speed;
                        item.radius = radius;
                        item.direction = direction;

                        // Advance cursor in state.
                        state.latest_gps_index = (long)id;

                    }
                    catch (SqlNullValueException e)
                    {
                        logger.Warn("Null value found in GpsLog record. exception: " + e.Message);
                    }

                    if (item != null)
                    {
                        yield return item;
                    }


                }
            }


        }

        connection.Close();

    }
}


public class SmartDispatchPlusV1Reader : IDataReader
{

    private string _connectionString;

    private string _database_schema;
    private string _database_name;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public SmartDispatchPlusV1Reader(string database_server, string database_name, string database_user, string database_password, string database_schema)
    {
        _database_schema = database_schema;
        _database_name = database_name;

        _connectionString = $"Host={database_server};Username={database_user};Password={database_password};Database={database_name};Search Path={database_schema},public;";    
    }


    public TestResult TestConnection()
    {

        try {
            using NpgsqlConnection connection = new NpgsqlConnection(this._connectionString);
            connection.Open();
            using (NpgsqlCommand command = new NpgsqlCommand("SELECT schema_name from information_schema.schemata where schema_name = @schema and catalog_name = @catalog", connection))
            {
                command.Parameters.AddWithValue("@schema", this._database_schema);
                command.Parameters.AddWithValue("@catalog", this._database_name);
                var val = command.ExecuteScalar();
                Console.WriteLine("Connection test successful. Value: " + val);

                if (val == null)
                {
                    return new TestResult(false, "Schema not found. " + this._database_schema);
                }
                return new TestResult(true, "All good.");
            }
        }
        catch (System.ArgumentException e)
        {
            return new TestResult(false, e.Message);
        }
        catch (System.Net.Sockets.SocketException se)
        {
            return new TestResult(false, se.Message);
        }
        catch (NpgsqlException e)
        {
            Console.WriteLine("Connection test failed. Exception: " + e.Message);
            return new TestResult(false, e.Message);
        }
    }

    public List<GroupAlias> GetGroupAliases() {       
        using NpgsqlConnection connection = new NpgsqlConnection(this._connectionString);
        connection.Open();

        var groups = new List<GroupAlias>();
        using (NpgsqlCommand command = new NpgsqlCommand("SELECT guid, alias from dbo.devicegroup where enableflag = true", connection))
        {
            using (NpgsqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string guid = reader.GetString(reader.GetOrdinal("guid"));
                    string alias = reader.GetString(reader.GetOrdinal("alias"));

                    groups.Add(new GroupAlias { guid = guid, alias = alias });

                }
            }
        }
        connection.Close();
        return groups;
    }

    async public IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date)
    {
        using DataPump.StateHandler state_handler = new DataPump.StateHandler("state.json");
        DataPump.State state = state_handler.LoadState();


        NpgsqlConnection connection = new NpgsqlConnection( this._connectionString);
        
        // Note: "lontitude" is the name of the longitude column.
        var query = "SELECT d.alias, g.id, g.deviceguid, g.deviceid, g.lontitude, g.latitude, g.speed," +
            " g.recvgpstime, g.happentime, g.activeflag, g.direction, g.description," +
            " g.gpstype, g.gpscontext, g.streetname, g.rssi" + 
            ", dg.alias as group_alias" +
            ", dg.guid as group_guid" +
            " FROM dbo.gpsinfo g JOIN dbo.device d ON d.guid = g.deviceguid" +
            " join dbo.devicegroup dg on dg.guid = d.groupguid" +
            " WHERE g.recvgpstime > @lower_date" +
            " and id > @latest_gps_index" +
            " ORDER BY g.recvgpstime asc" +
            " LIMIT 1000;";



        connection.Open();


        using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@lower_date", DateTime.SpecifyKind(lower_date, DateTimeKind.Unspecified));
            command.Parameters.AddWithValue("@latest_gps_index", state.latest_gps_index);

            using (NpgsqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    SmartDispatchPlusV1Record item = null;
                    try
                    {

                        item = new SmartDispatchPlusV1Record
                        {
                            latitude = reader.GetDouble(reader.GetOrdinal("latitude")),
                            longitude = reader.GetDouble(reader.GetOrdinal("lontitude")),


                            id = reader.GetInt64(reader.GetOrdinal("id")),
                            device_alias = reader.GetString(reader.GetOrdinal("alias")),
                            deviceid = reader.GetInt32(reader.GetOrdinal("deviceid")),
                            deviceguid = reader.GetString(reader.GetOrdinal("deviceguid")),
                            description = reader.GetString(reader.GetOrdinal("description")),
                            direction = reader.GetDouble(reader.GetOrdinal("direction")),
                            rssi = reader.GetDouble(reader.GetOrdinal("rssi")),
                            activeflag = reader.GetInt32(reader.GetOrdinal("activeflag")),

                            recvgpstime = reader.GetDateTime(reader.GetOrdinal("recvgpstime")),
                            happentime = reader.GetDateTime(reader.GetOrdinal("happentime")),
                            devicegroup_guid = reader.GetString(reader.GetOrdinal("group_guid")),
                            devicegroup_alias = reader.GetString(reader.GetOrdinal("group_alias"))
                        };

                        // Timestamps are naive in the database.
                        item.recvgpstime = DateTime.SpecifyKind(item.recvgpstime, DateTimeKind.Local);
                        item.happentime = DateTime.SpecifyKind(item.happentime, DateTimeKind.Local);


                        // Advance cursor in state.
                        state.latest_gps_index = item.id;


                    }
                    catch (NpgsqlException e)
                    {
                        logger.Warn("Failed parsing a result from querying SmartDispatchPlusV1 Database: " + e.Message);
                    }
                    

                    if (item != null)
                    {
                        yield return item;
                    }


                }
            }


        }

        connection.Close();

    }
}

public class SmartOneDispatchReader : IDataReader
{

    private string _connectionString;
    private string _database_schema;
    private string _database_name;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public SmartOneDispatchReader(string database_server, string database_name, string database_user, string database_password, string database_schema)
    {
        _database_name = database_name;
        _database_schema = database_schema;
        _connectionString = $"Host={database_server};Username={database_user};Password={database_password};Database={database_name};Search Path={database_schema},public;";
    }

    public TestResult TestConnection()
    {

        try
        {
            using NpgsqlConnection connection = new NpgsqlConnection(this._connectionString);
            connection.Open();
            using (NpgsqlCommand command = new NpgsqlCommand("SELECT schema_name from information_schema.schemata where schema_name = @schema and catalog_name = @catalog", connection))
            {
                command.Parameters.AddWithValue("@schema", this._database_schema);
                command.Parameters.AddWithValue("@catalog", this._database_name);
                var val = command.ExecuteScalar();
                Console.WriteLine("Connection test successful. Value: " + val);

                if (val == null)
                {
                    return new TestResult(false, "Schema not found. " + this._database_schema);
                }
                return new TestResult(true, "All good.");
            }
        }
        catch (System.ArgumentException e)
        {
            return new TestResult(false, e.Message);
        }
        catch (System.Net.Sockets.SocketException se)
        {
            return new TestResult(false, se.Message);
        }
        catch (NpgsqlException e)
        {
            Console.WriteLine("Connection test failed. Exception: " + e.Message);
            return new TestResult(false, e.Message);
        }
    }




    async public IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date)
    {
        using DataPump.StateHandler state_handler = new DataPump.StateHandler("state.json");
        DataPump.State state = state_handler.LoadState();


        NpgsqlConnection connection = new NpgsqlConnection(this._connectionString);

        // Note: "lontitude" is the name of the longitude column.
        var query = @"SELECT t1.device_alias, t1.car_make, t1.car_license_plate, t1.device_number,
                             t0.guid, t0.puc_id, t0.system_id, t0.device_id, t0.device_alias, t0.number_type,
                             t0.device_type, t0.staff_code, t0.gps_datetime, t0.gps_av, t0.long_we, t0.longitude,
                             t0.lat_ns, t0.latitude, t0.speed, t0.direction,
                             t0.state, t0.receive_datetime, t0.tsc_id, t0.channel_id, t0.rssi_up, 
                             t0.rssi_down, t0.power_mode, t0.electricity
                        FROM dbo.gps_location_data_base t0
                            join dbo.device_info t1 on t0.device_id = t1.device_id 
                                 and t0.puc_id = t1.puc_id
		                         and t0.system_id = t1.system_id
                       WHERE t0.receive_datetime > @lower_date
                       ORDER BY t0.receive_datetime asc
                       LIMIT 1000;";



        connection.Open();


        using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@lower_date", DateTime.SpecifyKind(lower_date, DateTimeKind.Unspecified));

            using (NpgsqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    SmartOneDispatchRecord item = null;
                    try
                    {

                        item = new SmartOneDispatchRecord
                        {
                            latitude = reader.GetDouble(reader.GetOrdinal("latitude")),
                            longitude = reader.GetDouble(reader.GetOrdinal("longitude")),
                            device_id = reader.GetString(reader.GetOrdinal("device_id")),
                            device_alias = reader.GetString(reader.GetOrdinal("device_alias")),
                            car_make = reader.GetString(reader.GetOrdinal("car_make")),
                            car_license_plate = reader.GetString(reader.GetOrdinal("car_license_plate")),
                            device_number = reader.GetString(reader.GetOrdinal("device_number")),
                            guid = reader.GetString(reader.GetOrdinal("guid")),
                            puc_id = reader.GetString(reader.GetOrdinal("puc_id")),
                            system_id = reader.GetString(reader.GetOrdinal("system_id")),
                            number_type = reader.GetInt32(reader.GetOrdinal("number_type")),
                            device_type = reader.GetInt32(reader.GetOrdinal("device_type")),


                            staff_code = reader.GetString(reader.GetOrdinal("staff_code")),
                            gps_datetime = reader.GetDateTime(reader.GetOrdinal("gps_datetime")),
                            gps_av = reader.GetString(reader.GetOrdinal("gps_av")),
                            long_we = reader.GetString(reader.GetOrdinal("long_we")),
                            lat_ns = reader.GetString(reader.GetOrdinal("lat_ns")),
                            speed = reader.GetDouble(reader.GetOrdinal("speed")),
                            direction = reader.GetDouble(reader.GetOrdinal("direction")),
                            state = reader.GetString(reader.GetOrdinal("state")),
                            receive_datetime = reader.GetDateTime(reader.GetOrdinal("receive_datetime")),
                            tsc_id = reader.GetInt32(reader.GetOrdinal("tsc_id")),
                            channel_id = reader.GetInt32(reader.GetOrdinal("channel_id")),
                            rssi_up = reader.GetInt32(reader.GetOrdinal("rssi_up")),
                            rssi_down = reader.GetInt32(reader.GetOrdinal("rssi_down")),
                            power_mode = reader.GetInt32(reader.GetOrdinal("power_mode")),
                            electricity = reader.GetInt32(reader.GetOrdinal("electricity"))
                        };

                        item.latitude = item.lat_ns.Equals("S", StringComparison.OrdinalIgnoreCase) ? Math.Abs(item.latitude) * -1 : item.latitude;
                        item.longitude = item.long_we.Equals("W", StringComparison.OrdinalIgnoreCase) ? Math.Abs(item.longitude) * -1 : item.longitude;
                        // Timestamps are naive in the database.
                        item.receive_datetime = DateTime.SpecifyKind(item.receive_datetime, DateTimeKind.Local);
                        item.gps_datetime = DateTime.SpecifyKind(item.gps_datetime, DateTimeKind.Local);

                    }
                    catch (NpgsqlException e)
                    {
                        logger.Warn("Failed parsing a result from querying SmartDispatchPlusV1 Database: " + e.Message);
                    }


                    if (item != null)
                    {
                        yield return item;
                    }


                }
            }


        }

        connection.Close();

    }
}


public class GroupedDataWriter : IDataWriter
{
    public readonly List<IDataWriter> writers;

    public void AddWriter(IDataWriter writer)
    {
        writers.Add(writer);
    }

    public async Task<int> PostObservation(ISourceRecord record)
    {
        writers.ForEach(async writer =>
                   await writer.PostObservation(record)
                          );
        return 0;
    } 

    public GroupedDataWriter()
    {
        writers = new List<IDataWriter>();
    }

}
public class GundiV2DataWriter : IDataWriter
{
    private readonly HttpClient _httpClient;
    private readonly string _destination;
    private readonly string _apikey;
    private HashSet<string> matchingGroups;

    private static Logger logger = LogManager.GetCurrentClassLogger();
    public GundiV2DataWriter(string destination = "https://sensors.api.gundiservice.org", string apikey = "SomethingFancy")
    {
        this._httpClient = new HttpClient();
        this._destination = destination;
        this._apikey = apikey;
        this._httpClient.DefaultRequestHeaders.Add("apikey", this._apikey);
        this._httpClient.DefaultRequestHeaders.Add("User-Agent", "Gundi Radio Service/2.0");

        this.matchingGroups = new HashSet<string>();
    }

    public void AddMatchingGroup(string group)
    {
        matchingGroups.Add(group);
    }

    public async Task<int> PostObservation(ISourceRecord record)
    {

        if (!matchingGroups.IsNullOrEmpty() && !matchingGroups.Contains(record.group_identifier))
        {
            return 0;
        }

        try
        {
            var observation = record.ToGundiV2Observation();

            List<GundiV2Observation> payload = new() { observation };

            logger.Info(JsonSerializer.Serialize<GundiV2Observation>(observation));
            var response = await _httpClient.PostAsJsonAsync<List<GundiV2Observation>>($"{this._destination}/v2/observations/", payload);
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


    public async Task<int> PostObservation(ISourceRecord record)
    {
        try
        {

            var observation = record.ToEarthRangerObservation();
            
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
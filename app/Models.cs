namespace DataPumpModels
{

    public interface ISourceRecord
    {
        public GundiV2Observation ToGundiV2Observation();
        public EarthRangerObservation ToEarthRangerObservation();

        public DateTime cursor_at { get; }   
    }

    public class SmartDispatchPlusV1Record : ISourceRecord
    {
        public string device_alias { get; set; } // "alias" is a csharp reserved word
        public DateTime recvgpstime { get; set; }
        public DateTime happentime { get; set; }

        public double latitude { get; set; }   
        public double longitude { get; set; }

        public long id { get; set; }
        public double speed { get; set; }
        public int deviceid { get; set; }
        public string deviceguid { get; set; }
        public string gpscontext { get; set; }
        public int gpstype { get; set; }
        public string streetname { get; set; }
        public double rssi { get; set; }
        public double direction { get; set; }
        public int activeflag { get; set; }
        public string description { get; set; }
       
        public DateTime cursor_at => recvgpstime;

        public EarthRangerObservation ToEarthRangerObservation()
        {

            var observation = new EarthRangerObservation();
            var location = new EarthRangerLocation();
            location.lat = (decimal)latitude;
            location.lon = (decimal)longitude;
            observation.location = location;

            observation.subject_name = device_alias;
            observation.manufacturer_id = deviceid.ToString();
            observation.recorded_at = happentime;

            observation.additional = new Dictionary<string, object>
                {
                    { "recvgpstime", recvgpstime },
                    { "id", id },
                    { "speed", speed },
                    { "deviceid", deviceid },
                    { "deviceguid", deviceguid },
                    { "gpscontext", gpscontext },
                    { "gpstype", gpstype },
                    { "streetname", streetname },
                    { "rssi", rssi },
                    { "direction", direction },
                    { "activeflag", activeflag },
                    { "description", description }
                };

            return observation;

        }

        public GundiV2Observation ToGundiV2Observation()
        {
            return new GundiV2Observation
            {
                recorded_at = happentime,
                source = deviceid.ToString(),
                source_name = device_alias,
                type = "location",
                location = new GundiV2Location
                {
                    lat = (decimal)latitude,
                    lon = (decimal)longitude
                },
                additional = new Dictionary<string, object>
                {
                    { "recvgpstime", recvgpstime },
                    { "id", id },
                    { "speed", speed },
                    { "deviceid", deviceid },
                    { "deviceguid", deviceguid },
                    { "gpscontext", gpscontext },
                    { "gpstype", gpstype },
                    { "streetname", streetname },
                    { "rssi", rssi },
                    { "direction", direction },
                    { "activeflag", activeflag },
                    { "description", description }
                }
            };
        }
    }

    public class SmartOneDispatchRecord : ISourceRecord
    {

        public string device_alias { get; set; }
        public string car_make { get; set; }
        public string car_license_plate { get; set; }
        public string device_number { get; set; }
        public string guid { get; set; }
        public string puc_id { get; set; }
        public string system_id { get; set; }
        public string device_id { get; set; }
        public int number_type { get; set; }
        public int device_type { get; set; }
        public string staff_code { get; set; }
        public DateTime gps_datetime { get; set; }
        public string gps_av { get; set; }
        public string long_we { get; set; }
        public double longitude { get; set; }
        public string lat_ns { get; set; }
        public double latitude { get; set; }
        public double speed { get; set; }
        public double direction { get; set; }
        public string state { get; set; }
        public DateTime receive_datetime { get; set; }
        public int tsc_id { get; set; }
        public int channel_id { get; set; }
        public int rssi_up { get; set; }
        public int rssi_down { get; set; }
        public int power_mode { get; set; }
        public int electricity { get; set; }

        public DateTime cursor_at => receive_datetime;

        public EarthRangerObservation ToEarthRangerObservation()
        {

            var observation = new EarthRangerObservation();
            //var location = new EarthRangerLocation();
            //location.lat = (decimal)latitude;
            //location.lon = (decimal)longitude;
            //observation.location = location;

            //observation.subject_name = device_alias;
            //observation.manufacturer_id = device_id.ToString();
            //observation.recorded_at = gps_datetime.spe;

            //observation.additional = new Dictionary<string, object>
            //    {
            //        { "recvgpstime", recvgpstime },
            //        { "id", id },
            //        { "speed", speed },
            //        { "deviceid", deviceid },
            //        { "deviceguid", deviceguid },
            //        { "gpscontext", gpscontext },
            //        { "gpstype", gpstype },
            //        { "streetname", streetname },
            //        { "rssi", rssi },
            //        { "direction", direction },
            //        { "activeflag", activeflag },
            //        { "description", description }
            //    };

            return observation;

        }

        public GundiV2Observation ToGundiV2Observation()
        {
            return new GundiV2Observation
            {
                recorded_at = gps_datetime,
                source = $"smartone-{system_id}-{device_id}",
                source_name = device_alias,
                type = "gps-radio",
                location = new GundiV2Location
                {
                    lat = (decimal)latitude,
                    lon = (decimal)longitude
                },
                additional = new Dictionary<string, object>
                {
                    { "receive_datetime", receive_datetime },
                    { "device_alias", device_alias },
                    { "car_make", car_make },
                    { "car_license_plate", car_license_plate },
                    { "device_number", device_number },
                    { "guid", guid },
                    { "puc_id", puc_id },
                    { "system_id", system_id },
                    { "device_id", device_id },
                    { "number_type", number_type },
                    { "device_type", device_type },
                    { "staff_code", staff_code },
                    { "gps_av", gps_av },
                    { "long_we", long_we },
                    { "lat_ns", lat_ns },
                    { "speed", speed },
                    { "direction", direction },
                    { "state", state },
                    { "tsc_id", tsc_id },
                    { "channel_id", channel_id },
                    { "rssi_up", rssi_up },
                    { "rssi_down", rssi_down },
                    { "power_mode", power_mode },
                    { "electricity", electricity }
                }
            };
        }
    }


    public class KenwoodGpsLogRecord : ISourceRecord
{
    public string name { get; set; }
    public long unit_id { get; set; }
    public string e_w { get; set; }
    public string n_s { get; set; }
    public DateTime created_at { get; set; }
    public DateTime updated_at { get; set; }
    public DateTime recorded_at { get; set; }
    public decimal latitude { get; set; }
    public decimal longitude { get; set; }
    public int system_id { get; set; }

        public DateTime cursor_at => recorded_at;

        public EarthRangerObservation ToEarthRangerObservation()
        {
            var observation = new EarthRangerObservation();
            var location = new EarthRangerLocation();
            location.lat = latitude;
            location.lon = longitude;
            observation.location = location;

            observation.subject_name = name;
            observation.manufacturer_id = $"kas20-{system_id}-{unit_id}";
            observation.recorded_at = recorded_at;

            observation.additional = new Dictionary<string, object>()
            {
                { "system_id", system_id },
                { "e_w", e_w },
                { "n_s", n_s },
                { "created_at", created_at },
                { "updated_at", updated_at},
                { "unit_id", unit_id }

            };

            return observation;


        }

        public GundiV2Observation ToGundiV2Observation()
    {
            return new GundiV2Observation
            {
                recorded_at = recorded_at,
                source = $"kas20-{system_id}-{unit_id}",
                source_name = this.name,
                type = "gps-radio",
                location = new GundiV2Location
                {
                    lat = this.latitude,
                    lon = this.longitude
                },
                additional = new Dictionary<string, object>
                {
                    { "system_id", this.system_id },
                    {"e_w", this.e_w },
                    { "n_s", this.n_s },
                    {"created_at", this.created_at },
                    {"updated_at", this.updated_at},
                    {"unit_id", this.unit_id }
                }
            };
        }
    }



    public class EarthRangerLocation
    {
        public decimal lon { get; set; }
        public decimal lat { get; set; }
    }

    public class EarthRangerObservation
    {
        public EarthRangerLocation location { get; set; }
        public DateTime recorded_at { get; set; }
        public string manufacturer_id { get; set; }
        public string subject_name { get; set; }
        public IDictionary<string, object> additional { get; set; }

    }

    public class GundiV2Location
    {
        public decimal lon { get; set; }
        public decimal lat { get; set; }

    }

    public class GundiV2Observation
    {
        public DateTime recorded_at { get; set; }
        public string source { get; set; }
        public string source_name { get; set; }

        public string type { get; set; }
        public GundiV2Location location { get; set; }
        public IDictionary<string, object> additional { get; set; }
    }

}
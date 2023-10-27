using System.Runtime.InteropServices;

namespace DataPumpModels {


    public class KenwoodGpsLogRecord
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

    public class GundiLocation
    {
        public decimal x { get; set; }
        public decimal y { get; set; }

    }
    public class GundiPosition
    {
        public DateTime recorded_at { get; set; }
        public string device_id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public GundiLocation location { get; set; }
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
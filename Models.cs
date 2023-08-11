using System.Runtime.InteropServices;

namespace Models {

    public class StatusData {
        public string version { get; set; }
        public int show_track_days { get; set; }
        public bool event_search_enabled { get; set; }
        public string site_name { get; set; }

    }
    public class StatusResponse {
        public StatusData data { get; set; }

    }

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
}
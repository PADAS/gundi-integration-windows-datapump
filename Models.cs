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
}
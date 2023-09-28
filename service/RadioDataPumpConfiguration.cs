using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace worker
{
    public class RadioDataPumpConfiguration
    {
        public string? destination { get; set; }
        public string? intervalMs { get; set; }
        public string? connectionString { get; set; }
        public string? earthranger_provider_key { get; set; }
        public string? earthranger_auth_token { get; set; }
        public string? gundi_apikey { get; set; }
        public string? kas20_system_id { get; set; }    
    }
}

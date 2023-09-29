using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace worker
{
    public class RadioServiceConfiguration
    {
        public string? destination { get; set; }
        public string? intervalMs { get; set; }
        public string? connectionString { 
            get {
                return $"Data Source={database_server};User ID={database_user};Password={database_password};Initial Catalog={database_name};TrustServerCertificate=True;";
            } 
        }

        public string? database_server { get; set; }
        public string? database_name { get; set; }
        public string? database_user { get; set; }
        public string? database_password { get; set; }
        public string? earthranger_provider_key { get; set; }
        public string? earthranger_auth_token { get; set; }
        public string? gundi_apikey { get; set; }
        public string? kas20_system_id { get; set; }    
    }
}

using NLog;
using service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace worker
{
    public class RadioServiceConfiguration
    {

        // Define an enum for coffee types
        public enum ReaderType
        {
            KAS20 = 0,
            SmartDispatchPlus = 1,
            SmartOneDispatch = 3,
            TrbonetPlus = 4
        }

    }

    public class AppSettingsManager : IDisposable
    {
        private string _filePath;
        private bool disposedValue;
        private AppSettings _value;
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public AppSettingsManager(string filePath)
        {
            _filePath = filePath;
        }

        // Save program state to a JSON file
        public void SaveValue()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true  };
                string json = JsonSerializer.Serialize(_value, options);
                File.WriteAllText(_filePath, json);
                logger.Debug("AppSettings saved successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving AppSettings: {ex.Message}");
            }
        }

        // Load program state from a JSON file
        public AppSettings LoadValue()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    _value = JsonSerializer.Deserialize<AppSettings>(json);
                    logger.Info("State loaded successfully.");

                }
                else
                {
                    logger.Debug("State file does not exist. Creating a new one.");
                    _value = new AppSettings()
                    {
                        Logging = new Dictionary<string, object>()

                        {
                            ["LogLevel"] = new Dictionary<string, string>()
                                {
                                    {"Default", "Information"},
                                    {"Microsoft.Hosting.Lifetime", "Information" }
                                }
                        }

                        
                    }; // You can customize this to initialize the state.

                }
                return _value;
            }
            catch (Exception ex)
            {
                logger.Debug($"Error loading service state: {ex.Message}");
                return new AppSettings(); // Return a default settings if loading fails.
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;

                this.SaveValue();
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~StateHandler()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    // Define your program state class
    public class AppSettings
    {
        public RouteConfiguration? RouteConfiguration { get; set; } = new RouteConfiguration();

        public IDictionary<string, object>? Logging { get; set; }
    }

}

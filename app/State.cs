using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DataPump
{
    public class StateHandler : IDisposable
    {
        private string stateFilePath;
        private bool disposedValue;
        private State _state;
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public StateHandler(string filePath)
        {
            stateFilePath = filePath;
        }

        // Save program state to a JSON file
        public void SaveState()
        {
            try
            {
                if (!_state.dirty)
                {
                    logger.Debug("Program state not dirty. Skipping save.");
                    return;
                }
                string json = JsonSerializer.Serialize(_state);
                File.WriteAllText(stateFilePath, json);
                logger.Debug("Program state saved successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving program state: {ex.Message}");
            }
        }

        // Load program state from a JSON file
        public State LoadState()
        {
            try
            {
                if (File.Exists(stateFilePath))
                {
                    string json = File.ReadAllText(stateFilePath);
                    _state = JsonSerializer.Deserialize<State>(json);
                    logger.Debug("Program state loaded successfully.");
                   
                }
                else
                {
                    logger.Debug("Program state file does not exist. Creating a new one.");
                    _state = new State()
                    {
                        latest_gps_index = 0
                    }; // You can customize this to initialize the state.

                }
                _state.dirty = false;
                return _state;
            }
            catch (Exception ex)
            {
                logger.Debug($"Error loading program state: {ex.Message}");
                return new State(); // Return a default state if loading fails.
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

                this.SaveState();
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
    public class State
    {
        private long _latest_gps_index;
        private bool _dirty = false;

        // Add properties and fields to represent your program's state
        public long latest_gps_index {
            get => _latest_gps_index;
            set
            {
                _latest_gps_index = value;
                _dirty = true;
            }
        }
        public bool dirty
        {
            get => _dirty;
            set => _dirty = value;
        }
    }
}

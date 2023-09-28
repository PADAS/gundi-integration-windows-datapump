using System.Text.Json;
using NLog;

namespace DataPump
{
    public class RadioDataPump
    {

        int _intervalMs = 5000;

        private static Logger logger = LogManager.GetCurrentClassLogger();
        public RadioDataPump(int intervalMs = 5000)
        {
            _intervalMs = intervalMs;

        }
        public async Task<int> Run(IDataReader reader, IDataWriter writer, CancellationToken cancellationToken)
        {
            var val = await this.Pump(reader, writer, cancellationToken);
            return 1;
        }

        private async Task<int> Pump(IDataReader reader, IDataWriter writer, CancellationToken cancellationToken)
        {
            var lower_date = DateTime.UtcNow.AddMinutes(-2880);
            while (!cancellationToken.IsCancellationRequested) 
            {
                await foreach (var item in reader.ReadNew(lower_date))
                {
                    logger.Debug("item: " + JsonSerializer.Serialize(item));

                    await writer.PostObservation(item);

                    lower_date = item.updated_at > lower_date ? item.updated_at : lower_date;
                }

                await Task.Delay(this._intervalMs).ContinueWith(_ => logger.Info("Continuing!"));
            }
            return 0;
        }
    }
}
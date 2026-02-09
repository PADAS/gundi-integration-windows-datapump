using System.Text.Json;
using NLog;

namespace DataPump
{
    public class RadioDataPump
    {
        private const int MaxDbRetries = 5;
        private const int BaseRetryDelayMs = 5000;

        int _intervalMs = 5000;

        private static Logger logger = LogManager.GetCurrentClassLogger();
        public RadioDataPump(int intervalMs = 5000)
        {
            _intervalMs = intervalMs;
        }

        public async Task<int> Run(IDataReader reader, IDataWriter writer, CancellationToken cancellationToken)
        {
            var val = await this.Pump(reader, writer, cancellationToken);
            return val;
        }

        private async Task<int> Pump(IDataReader reader, IDataWriter writer, CancellationToken cancellationToken)
        {
            var lower_date = DateTime.UtcNow.AddMinutes(-2880);
            int consecutiveDbErrors = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var item in reader.ReadNew(lower_date))
                    {
                        logger.Debug("item: " + JsonSerializer.Serialize(item));

                        await writer.PostObservation(item, cancellationToken);

                        lower_date = item.cursor_at > lower_date ? item.cursor_at : lower_date;
                    }

                    // Reset error counter on successful read
                    consecutiveDbErrors = 0;

                    await Task.Delay(this._intervalMs, cancellationToken).ContinueWith(_ => logger.Debug("Tick."));
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested - let it propagate
                    throw;
                }
                catch (Exception ex) when (IsTransientDatabaseError(ex))
                {
                    consecutiveDbErrors++;
                    int delayMs = CalculateRetryDelay(consecutiveDbErrors);

                    logger.Warn($"Database error (attempt {consecutiveDbErrors}/{MaxDbRetries}): {ex.Message}. Retrying in {delayMs / 1000}s...");

                    if (consecutiveDbErrors >= MaxDbRetries)
                    {
                        logger.Error($"Max database retries ({MaxDbRetries}) exceeded. Last error: {ex.Message}");
                        throw;
                    }

                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            return 0;
        }

        private static bool IsTransientDatabaseError(Exception ex)
        {
            // Check for SQL Server transient errors
            if (ex.GetType().Name == "SqlException" || ex.GetType().FullName?.Contains("SqlException") == true)
            {
                return true;
            }

            // Check for PostgreSQL transient errors
            if (ex.GetType().Name == "NpgsqlException" || ex.GetType().FullName?.Contains("NpgsqlException") == true)
            {
                return true;
            }

            // Check for general network/timeout errors
            if (ex is System.Net.Sockets.SocketException ||
                ex is TimeoutException ||
                ex is System.IO.IOException)
            {
                return true;
            }

            // Check inner exception
            if (ex.InnerException != null)
            {
                return IsTransientDatabaseError(ex.InnerException);
            }

            return false;
        }

        private static int CalculateRetryDelay(int attemptNumber)
        {
            // Exponential backoff: 5s, 10s, 20s, 40s, 80s (capped)
            int delay = BaseRetryDelayMs * (int)Math.Pow(2, attemptNumber - 1);
            return Math.Min(delay, 80000); // Cap at 80 seconds
        }
    }
}
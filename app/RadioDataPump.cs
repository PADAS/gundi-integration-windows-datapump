using System.Text.Json;
using DataPumpModels;
using NLog;
using Polly.CircuitBreaker;

namespace DataPump
{
    public class RadioDataPump
    {
        private const int MaxDbRetries = 5;
        private const int BaseRetryDelayMs = 5000;
        private static readonly TimeSpan CircuitBreakerPauseTime = TimeSpan.FromMinutes(1);

        private readonly int _intervalMs;
        private readonly int _batchSize;

        private static Logger logger = LogManager.GetCurrentClassLogger();
        public RadioDataPump(int intervalMs = 5000, int batchSize = 25)
        {
            _intervalMs = intervalMs;
            _batchSize = batchSize;
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
            int consecutivePostTimeouts = 0;
            var batch = new List<ISourceRecord>(_batchSize);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // A batch whose post timed out stays in `batch`. Send it
                    // before reading anything new: the reader has already
                    // moved its cursor past those records, so re-reading
                    // can't be relied on to return them.
                    if (batch.Count > 0)
                    {
                        lower_date = await FlushBatch(batch, writer, lower_date, cancellationToken);
                        consecutivePostTimeouts = 0;
                    }

                    await foreach (var item in reader.ReadNew(lower_date))
                    {
                        // Check for cancellation between records
                        cancellationToken.ThrowIfCancellationRequested();

                        logger.Debug("item: " + JsonSerializer.Serialize(item));
                        batch.Add(item);

                        if (batch.Count >= _batchSize)
                        {
                            lower_date = await FlushBatch(batch, writer, lower_date, cancellationToken);
                            consecutivePostTimeouts = 0;
                        }
                    }

                    // Flush remaining records after enumeration completes
                    if (batch.Count > 0)
                    {
                        lower_date = await FlushBatch(batch, writer, lower_date, cancellationToken);
                        consecutivePostTimeouts = 0;
                    }

                    // Reset error counter on successful read cycle
                    consecutiveDbErrors = 0;

                    // Pause between read cycles. (Previously logged "Tick."
                    // here every cycle as a heartbeat; replaced by
                    // HeartbeatService, which only logs during idle
                    // periods and avoids the per-cycle log spam.)
                    //
                    // Swallow OperationCanceledException here on purpose:
                    // when the stopping token fires during this delay, we
                    // want the outer while-loop's token check to handle it
                    // and return cleanly. Letting the exception propagate
                    // would route through the surrounding catch which
                    // re-throws — turning a normal shutdown into a thrown
                    // exception out of pump.Run, which the tests (and the
                    // production worker's outer loop) treat as a fault.
                    try
                    {
                        await Task.Delay(this._intervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested - let it propagate
                    throw;
                }
                catch (PostTimeoutException ex)
                {
                    // The batch never made it. It's still in `batch` and
                    // lower_date wasn't advanced; back off, then the top of
                    // the loop posts it again. No retry limit: giving up
                    // would mean dropping data, and the dashboard shows the
                    // error while this goes on.
                    consecutivePostTimeouts++;
                    int delayMs = CalculatePostRetryDelay(consecutivePostTimeouts);

                    logger.Warn($"Post timed out (attempt {consecutivePostTimeouts}): {ex.Message} Retrying the same batch in {delayMs / 1000.0:0.#}s...");

                    await Task.Delay(delayMs, cancellationToken);
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

        /// <summary>
        /// Flushes the batch to the writer and returns the updated cursor value.
        /// Clears the batch unless the post timed out; then the records stay
        /// in it so the caller can post them again.
        /// </summary>
        private async Task<DateTime> FlushBatch(List<ISourceRecord> batch, IDataWriter writer,
            DateTime lower_date, CancellationToken cancellationToken)
        {
            try
            {
                logger.Debug($"Flushing batch of {batch.Count} records");
                await writer.PostObservations(batch, cancellationToken);

                // Only advance cursor after successful batch post
                var maxCursor = batch.Max(r => r.cursor_at);
                batch.Clear();
                return maxCursor > lower_date ? maxCursor : lower_date;
            }
            catch (OperationCanceledException)
            {
                // Service shutdown - propagate
                throw;
            }
            catch (PostTimeoutException)
            {
                // Keep the batch and don't advance the cursor; Pump posts it again.
                throw;
            }
            catch (BrokenCircuitException)
            {
                // Circuit breaker is open - API is unhealthy
                // Don't advance cursor, pause processing, and retry from this point
                logger.Warn($"Circuit breaker open - pausing processing for {CircuitBreakerPauseTime.TotalSeconds}s before retrying...");
                batch.Clear();
                await Task.Delay(CircuitBreakerPauseTime, cancellationToken);
                throw; // Re-throw to break out of foreach
            }
            catch (Exception ex)
            {
                // Writer error for this batch - log and advance cursor to skip
                logger.Warn($"Failed to post batch of {batch.Count} records: {ex.Message}");
                // Advance cursor to skip this problematic batch
                var maxCursor = batch.Max(r => r.cursor_at);
                batch.Clear();
                return maxCursor > lower_date ? maxCursor : lower_date;
            }
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

        private int CalculatePostRetryDelay(int attemptNumber)
        {
            // Exponential backoff from the poll interval: with the default
            // 5s interval that's 5s, 10s, 20s, 40s, then 80s (capped).
            long delay = (long)_intervalMs << Math.Min(attemptNumber - 1, 16);
            return (int)Math.Min(delay, 80000);
        }
    }
}
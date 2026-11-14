using DataPumpModels;

namespace DataPump
{
    /// <summary>
    /// Drops records that would produce an observation with the same
    /// (source, recorded_at) as one already seen. Gundi treats that pair
    /// as the identity of an observation, so two source rows that share
    /// it are one observation downstream, even when the rows have
    /// different IDs.
    ///
    /// Keys are remembered for a window of elapsed time since the pump
    /// saw them, not a window of recorded_at time. recorded_at comes from
    /// the radio's clock and readers don't return rows in recorded_at
    /// order, so evicting by it either keeps keys forever (one record with
    /// a far-future timestamp would stall eviction for the life of the
    /// process) or forgets keys too early. Duplicate rows are written
    /// close together and read close together, so elapsed time since
    /// seen is what actually bounds how far apart two copies arrive.
    /// Because keys are queued in the order they're seen, eviction is a
    /// simple FIFO and memory is bounded by one window of throughput.
    /// </summary>
    public class ObservationDeduplicator
    {
        public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(6);

        private readonly long _windowMs;
        private readonly Func<long> _nowMs;
        private readonly HashSet<(string source, long recordedAtTicks)> _seen = new();
        private readonly Queue<((string source, long recordedAtTicks) key, long seenAtMs)> _order = new();

        /// <param name="window">How long a key is remembered after it was first seen.</param>
        /// <param name="nowMs">
        /// Monotonic milliseconds. Defaults to Environment.TickCount64, which
        /// doesn't jump with wall-clock changes. Injectable for tests.
        /// </param>
        public ObservationDeduplicator(TimeSpan? window = null, Func<long>? nowMs = null)
        {
            _windowMs = (long)(window ?? DefaultWindow).TotalMilliseconds;
            _nowMs = nowMs ?? (() => Environment.TickCount64);
        }

        public int Count => _seen.Count;

        /// <summary>
        /// Returns true if the record is new and has been remembered;
        /// false if an observation with the same source and recorded_at
        /// was seen within the window.
        /// </summary>
        public bool TryAdd(ISourceRecord record)
        {
            var now = _nowMs();
            Prune(now);

            var obs = record.ToGundiV2Observation();
            var key = (obs.source ?? "", obs.recorded_at.Ticks);

            if (!_seen.Add(key))
            {
                return false;
            }

            _order.Enqueue((key, now));
            return true;
        }

        private void Prune(long now)
        {
            // Prune runs before every add, so a key is always dequeued
            // before it can be re-added: each key appears at most once in
            // the queue, and removing it from the set here is exact.
            var cutoff = now - _windowMs;
            while (_order.Count > 0 && _order.Peek().seenAtMs <= cutoff)
            {
                _seen.Remove(_order.Dequeue().key);
            }
        }
    }
}

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
    /// Keys are kept for a sliding window measured in recorded_at time
    /// (not wall-clock), so a duplicate that arrives in a later poll is
    /// still caught. Once the newest recorded_at seen is more than the
    /// window ahead of a key, that key is forgotten.
    /// </summary>
    public class ObservationDeduplicator
    {
        public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(48);

        private readonly TimeSpan _window;
        private readonly HashSet<(string source, long recordedAtTicks)> _seen = new();
        private readonly Queue<(string source, long recordedAtTicks)> _order = new();
        private long _newestTicks = long.MinValue;

        public ObservationDeduplicator(TimeSpan? window = null)
        {
            _window = window ?? DefaultWindow;
        }

        public int Count => _seen.Count;

        /// <summary>
        /// Returns true if the record is new and has been remembered;
        /// false if an observation with the same source and recorded_at
        /// was already seen within the window.
        /// </summary>
        public bool TryAdd(ISourceRecord record)
        {
            var obs = record.ToGundiV2Observation();
            var key = (obs.source ?? "", obs.recorded_at.Ticks);

            if (!_seen.Add(key))
            {
                return false;
            }

            _order.Enqueue(key);
            if (key.Item2 > _newestTicks)
            {
                _newestTicks = key.Item2;
                Prune();
            }
            return true;
        }

        private void Prune()
        {
            var cutoff = _newestTicks - _window.Ticks;
            // Keys are enqueued in arrival order, not recorded_at order, so
            // only the head is checked each time. An out-of-order old key
            // behind a newer one lingers until the newer one expires; that
            // costs a little memory, never a wrong answer.
            while (_order.Count > 0 && _order.Peek().recordedAtTicks < cutoff)
            {
                _seen.Remove(_order.Dequeue());
            }
        }
    }
}

using DataPump;
using DataPumpModels;
using Moq;

namespace general_tests
{
    public class ObservationDeduplicatorTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Local);

        private static SmartDispatchPlusV1Record Row(long id, int deviceId, DateTime happentime) => new()
        {
            id = id,
            deviceid = deviceId,
            happentime = happentime,
            recvgpstime = happentime,
            device_alias = "unit",
            deviceguid = "guid",
            gpscontext = "",
            streetname = "",
            description = "",
        };

        [Fact]
        public void SameSourceAndTime_DifferentRowId_IsDuplicate()
        {
            var d = new ObservationDeduplicator();
            Assert.True(d.TryAdd(Row(1, 7, T0)));
            Assert.False(d.TryAdd(Row(2, 7, T0)));
            Assert.Equal(1, d.Count);
        }

        [Fact]
        public void DifferentSourceOrTime_IsNotDuplicate()
        {
            var d = new ObservationDeduplicator();
            Assert.True(d.TryAdd(Row(1, 7, T0)));
            Assert.True(d.TryAdd(Row(2, 8, T0)));
            Assert.True(d.TryAdd(Row(3, 7, T0.AddSeconds(1))));
            Assert.Equal(3, d.Count);
        }

        /// <summary>A manually advanced monotonic clock, in milliseconds.</summary>
        private class FakeClock
        {
            public long Ms;
            public void Advance(TimeSpan t) => Ms += (long)t.TotalMilliseconds;
        }

        [Fact]
        public void KeyOlderThanWindow_IsForgotten()
        {
            var clock = new FakeClock();
            var d = new ObservationDeduplicator(TimeSpan.FromHours(1), () => clock.Ms);
            Assert.True(d.TryAdd(Row(1, 7, T0)));

            clock.Advance(TimeSpan.FromHours(1));

            // Seen a full window ago, so it's forgotten and accepted again.
            Assert.True(d.TryAdd(Row(2, 7, T0)));
            Assert.Equal(1, d.Count);
        }

        [Fact]
        public void KeyInsideWindow_IsStillRemembered()
        {
            var clock = new FakeClock();
            var d = new ObservationDeduplicator(TimeSpan.FromHours(1), () => clock.Ms);
            Assert.True(d.TryAdd(Row(1, 7, T0)));

            clock.Advance(TimeSpan.FromMinutes(59));

            Assert.False(d.TryAdd(Row(2, 7, T0)));
        }

        [Fact]
        public void OutOfOrderRecordedAt_DoesNotAffectEviction()
        {
            // The ordering from review: recorded_at T+48h, then T, then
            // T+49h, all seen within the window. A second T is a true
            // repeat of something already sent, so it must be rejected,
            // and nothing is evicted early because of recorded_at.
            var clock = new FakeClock();
            var d = new ObservationDeduplicator(TimeSpan.FromHours(48), () => clock.Ms);
            Assert.True(d.TryAdd(Row(1, 7, T0.AddHours(48))));
            Assert.True(d.TryAdd(Row(2, 7, T0)));
            Assert.True(d.TryAdd(Row(3, 7, T0.AddHours(49))));
            Assert.False(d.TryAdd(Row(4, 7, T0)));
            Assert.Equal(3, d.Count);
        }

        [Fact]
        public void FarFutureRecordedAt_DoesNotStallEviction()
        {
            // A radio with a bad clock reports a timestamp decades ahead.
            // Memory must still be released as the window passes.
            var clock = new FakeClock();
            var d = new ObservationDeduplicator(TimeSpan.FromHours(1), () => clock.Ms);
            Assert.True(d.TryAdd(Row(1, 7, new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Local))));
            for (int i = 0; i < 100; i++)
            {
                Assert.True(d.TryAdd(Row(10 + i, 7, T0.AddSeconds(i))));
            }
            Assert.Equal(101, d.Count);

            clock.Advance(TimeSpan.FromHours(1));
            Assert.True(d.TryAdd(Row(999, 8, T0)));

            // Everything older than the window is gone; only the new key remains.
            Assert.Equal(1, d.Count);
        }

        [Fact]
        public async Task Pump_DropsDuplicateBeforePosting()
        {
            var now = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Utc);
            async IAsyncEnumerable<ISourceRecord> Rows()
            {
                yield return Row(1, 7, now);
                yield return Row(2, 7, now);                 // duplicate of row 1
                yield return Row(3, 7, now.AddMinutes(1));
                yield return Row(4, 9, now);                 // other device, same time
                await Task.CompletedTask;
            }
            async IAsyncEnumerable<ISourceRecord> Empty()
            {
                await Task.CompletedTask;
                yield break;
            }

            var reader = new Mock<IDataReader>();
            reader.SetupSequence(r => r.ReadNew(It.IsAny<DateTime>()))
                .Returns(Rows()).Returns(Empty()).Returns(Empty()).Returns(Empty()).Returns(Empty());

            var postedIds = new List<long>();
            var writer = new Mock<IDataWriter>();
            writer.Setup(w => w.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ISourceRecord>, CancellationToken>((records, _) =>
                    postedIds.AddRange(records.Cast<SmartDispatchPlusV1Record>().Select(r => r.id)))
                .Returns(Task.FromResult(0));

            var pump = new RadioDataPump(200, 25);
            var cts = new CancellationTokenSource(700);
            await pump.Run(reader.Object, writer.Object, cts.Token);

            Assert.Equal(new long[] { 1, 3, 4 }, postedIds);
        }

        [Fact]
        public async Task Pump_SkipsUnconvertibleRecordAndKeepsPumping()
        {
            // (decimal)1e30 and (decimal)NaN throw OverflowException in
            // ToGundiV2Observation. The pump must skip just those rows and
            // still post the valid rows around them.
            var now = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Utc);
            var tooBig = Row(2, 7, now.AddSeconds(1));
            tooBig.latitude = 1e30;
            var notANumber = Row(3, 7, now.AddSeconds(2));
            notANumber.longitude = double.NaN;

            async IAsyncEnumerable<ISourceRecord> Rows()
            {
                yield return Row(1, 7, now);
                yield return tooBig;
                yield return notANumber;
                yield return Row(4, 7, now.AddSeconds(3));
                await Task.CompletedTask;
            }
            async IAsyncEnumerable<ISourceRecord> Empty()
            {
                await Task.CompletedTask;
                yield break;
            }

            var reader = new Mock<IDataReader>();
            reader.SetupSequence(r => r.ReadNew(It.IsAny<DateTime>()))
                .Returns(Rows()).Returns(Empty()).Returns(Empty()).Returns(Empty()).Returns(Empty());

            var postedIds = new List<long>();
            var writer = new Mock<IDataWriter>();
            writer.Setup(w => w.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ISourceRecord>, CancellationToken>((records, _) =>
                    postedIds.AddRange(records.Cast<SmartDispatchPlusV1Record>().Select(r => r.id)))
                .Returns(Task.FromResult(0));

            var pump = new RadioDataPump(200, 25);
            var cts = new CancellationTokenSource(700);

            // Completes normally: the bad rows don't escape the pump.
            await pump.Run(reader.Object, writer.Object, cts.Token);

            Assert.Equal(new long[] { 1, 4 }, postedIds);
        }
    }
}

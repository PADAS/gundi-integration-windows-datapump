using DataPump;
using DataPumpModels;
using Moq;
using AutoFixture;
using System.Runtime.CompilerServices;
using worker;
using Polly.CircuitBreaker;

namespace general_tests
{
    public class GpsLogTest
    {
        [Fact]
        public void Test1()
        {
            RadioDataPump pump = new RadioDataPump();
            Assert.True(true);

        }
    }

    public class RadioServiceConfigurationTest
    {
        [Fact]
        public void Test1()
        {
            RadioServiceConfiguration config = new RadioServiceConfiguration();
            Assert.True(true);
        }
    }

    public class RadioServiceTest
    {
        readonly DateTime date_1 = DateTime.SpecifyKind(DateTime.Parse("2024-01-21 15:00:00"), DateTimeKind.Utc);
        readonly DateTime date_2 = DateTime.SpecifyKind(DateTime.Parse("2024-01-23 15:00:00"), DateTimeKind.Utc);
        readonly DateTime date_3 = DateTime.SpecifyKind(DateTime.Parse("2024-01-25 15:00:00"), DateTimeKind.Utc);

        private readonly Fixture _fixture = new();

        private async IAsyncEnumerable<ISourceRecord> MockResponse1()
         {

            
            yield return new SmartDispatchPlusV1Record
            {
                device_alias = "test",
                recvgpstime = date_1,
                happentime = date_1,
                latitude = 0,
                longitude = 0,
                id = 0,
                speed = 0,
                deviceid = 0,
                deviceguid = "test",
                gpscontext = "test",
                gpstype = 0,
                streetname = "test",
                rssi = 0,
                direction = 0,
                activeflag = 0,
                description = "test"
            };
            yield return new SmartDispatchPlusV1Record
            {
                device_alias = "test",
                recvgpstime = date_2,
                happentime = date_2,
                latitude = 0,
                longitude = 0,
                id = 0,
                speed = 0,
                deviceid = 0,
                deviceguid = "test",
                gpscontext = "test",
                gpstype = 0,
                streetname = "test",
                rssi = 0,
                direction = 0,
                activeflag = 0,
                description = "test"
            };

            yield return new SmartDispatchPlusV1Record
            {
                device_alias = "test",
                recvgpstime = date_3,
                happentime = date_3,
                latitude = 0,
                longitude = 0,
                id = 0,
                speed = 0,
                deviceid = 0,
                deviceguid = "test",
                gpscontext = "test",
                gpstype = 0,
                streetname = "test",
                rssi = 0,
                direction = 0,
                activeflag = 0,
                description = "test"
            };
        }


        private async IAsyncEnumerable<ISourceRecord> MockResponse2()
        {
            if (1 > 2)
            {
                yield return new SmartDispatchPlusV1Record
                {
                    device_alias = "test",
                    recvgpstime = date_3,
                    happentime = date_3,
                    latitude = 0,
                    longitude = 0,
                    id = 0,
                    speed = 0,
                    deviceid = 0,
                    deviceguid = "test",
                    gpscontext = "test",
                    gpstype = 0,
                    streetname = "test",
                    rssi = 0,
                    direction = 0,
                    activeflag = 0,
                    description = "test"
                };
            }

        }


            [Fact]
        public async Task TestCursorAdvance()
        {

            var reader_mocker = new Mock<IDataReader>();
            reader_mocker.Setup(f => f.ReadNew(It.IsAny<DateTime>())).Returns(MockResponse1());
            IDataReader mock_reader = reader_mocker.Object;

            var writer_mocker = new Mock<IDataWriter>();
            writer_mocker.Setup(f => f.PostObservation(It.IsAny<ISourceRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(1));


            IDataWriter mock_writer = writer_mocker.Object;

            DateTime lower_date = DateTime.SpecifyKind(DateTime.Parse("2024-01-21 15:00:00"), DateTimeKind.Utc);


            await foreach (var item in mock_reader.ReadNew(lower_date))
            {

                await mock_writer.PostObservation(item, CancellationToken.None);
                Console.WriteLine(item.cursor_at);

                lower_date = item.cursor_at > lower_date ? item.cursor_at : lower_date;
            }

            writer_mocker.Verify(f => f.PostObservation(It.IsAny<ISourceRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            Assert.True(lower_date == DateTime.SpecifyKind(DateTime.Parse("2024-01-25 15:00:00"), DateTimeKind.Utc));
        }


        [Fact]
        public async Task TestDataPump()
        {

            var reader_mocker = new Mock<IDataReader>();
            reader_mocker.SetupSequence(f => f.ReadNew(It.IsAny<DateTime>())).Returns(MockResponse1()).Returns(MockResponse2()).Returns(MockResponse2());
            IDataReader mock_reader = reader_mocker.Object;

            // Capture batch sizes at invocation time (before the list is cleared)
            var capturedBatchSizes = new List<int>();

            var writer_mocker = new Mock<IDataWriter>();
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ISourceRecord>, CancellationToken>((records, ct) => capturedBatchSizes.Add(records.Count))
                .Returns(Task.FromResult(0));


            IDataWriter mock_writer = writer_mocker.Object;


            RadioDataPump pump = new RadioDataPump(1000, 25);

            CancellationTokenSource tokenSource= new CancellationTokenSource();
            tokenSource.CancelAfter(3000);


            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // With batch size 25 and 3 records, we expect a single batch flush with 3 records
            Assert.Single(capturedBatchSizes);
            Assert.Equal(3, capturedBatchSizes[0]);
        }

        [Fact]
        public async Task TestDataPumpBatching()
        {
            // Test that records are batched correctly when batch size is smaller than record count
            var reader_mocker = new Mock<IDataReader>();
            reader_mocker.SetupSequence(f => f.ReadNew(It.IsAny<DateTime>())).Returns(MockResponse1()).Returns(MockResponse2()).Returns(MockResponse2());
            IDataReader mock_reader = reader_mocker.Object;

            // Capture batch sizes at invocation time (before the list is cleared)
            var capturedBatchSizes = new List<int>();

            var writer_mocker = new Mock<IDataWriter>();
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ISourceRecord>, CancellationToken>((records, ct) => capturedBatchSizes.Add(records.Count))
                .Returns(Task.FromResult(0));

            IDataWriter mock_writer = writer_mocker.Object;

            // Batch size of 2, with 3 records should result in 2 batch flushes
            RadioDataPump pump = new RadioDataPump(1000, 2);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(3000);

            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // First batch of 2, then remaining 1
            Assert.Equal(2, capturedBatchSizes.Count);
            Assert.Equal(2, capturedBatchSizes[0]); // First batch
            Assert.Equal(1, capturedBatchSizes[1]); // Remaining record
        }

        [Fact]
        public async Task TestServerErrorSkipsBatch()
        {
            // Test that a server error (HttpRequestException) causes the batch to be skipped
            // and cursor advances to prevent infinite retries on bad data
            var reader_mocker = new Mock<IDataReader>();
            reader_mocker.SetupSequence(f => f.ReadNew(It.IsAny<DateTime>()))
                .Returns(MockResponse1())
                .Returns(MockResponse2())
                .Returns(MockResponse2());
            IDataReader mock_reader = reader_mocker.Object;

            int postObservationsCallCount = 0;

            var writer_mocker = new Mock<IDataWriter>();
            // First call throws HttpRequestException (simulating 500/502/503 after retries exhausted)
            // Second call succeeds (if there were more records)
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ISourceRecord>, CancellationToken>((records, ct) => postObservationsCallCount++)
                .Returns<IReadOnlyList<ISourceRecord>, CancellationToken>((records, ct) =>
                {
                    if (postObservationsCallCount == 1)
                        throw new HttpRequestException("Server returned 503 Service Unavailable");
                    return Task.FromResult(0);
                });

            IDataWriter mock_writer = writer_mocker.Object;

            RadioDataPump pump = new RadioDataPump(1000, 25);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(3000);

            // Should not throw - error is caught and batch is skipped
            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // Verify PostObservations was called (batch attempted)
            Assert.True(postObservationsCallCount >= 1, "PostObservations should have been called at least once");
        }

        [Fact]
        public async Task TestCircuitBreakerPausesProcessing()
        {
            // Test that BrokenCircuitException causes processing to pause
            var reader_mocker = new Mock<IDataReader>();
            reader_mocker.SetupSequence(f => f.ReadNew(It.IsAny<DateTime>()))
                .Returns(MockResponse1())
                .Returns(MockResponse2())
                .Returns(MockResponse2());
            IDataReader mock_reader = reader_mocker.Object;

            int postObservationsCallCount = 0;

            var writer_mocker = new Mock<IDataWriter>();
            // Throws BrokenCircuitException (circuit breaker is open)
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ISourceRecord>, CancellationToken>((records, ct) => postObservationsCallCount++)
                .ThrowsAsync(new BrokenCircuitException("Circuit breaker is open"));

            IDataWriter mock_writer = writer_mocker.Object;

            RadioDataPump pump = new RadioDataPump(500, 25);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            // Short timeout - circuit breaker pause is 60s, so this will cancel during the pause
            tokenSource.CancelAfter(2000);

            // The pump should throw OperationCanceledException when cancelled during circuit breaker pause
            // This is expected behavior - the service is shutting down gracefully
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await pump.Run(mock_reader, mock_writer, tokenSource.Token));

            // Verify PostObservations was called at least once before the circuit breaker opened
            Assert.True(postObservationsCallCount >= 1, "PostObservations should have been called at least once");
        }

        /// <summary>
        /// Behaves like the real readers: yields rows with id above its
        /// cursor and advances the cursor before each yield.
        /// </summary>
        private class CursorAdvancingReader : IDataReader
        {
            private readonly List<SmartDispatchPlusV1Record> _rows;
            private long _cursor;
            public List<DateTime> LowerDates { get; } = new();

            public CursorAdvancingReader(List<SmartDispatchPlusV1Record> rows) => _rows = rows;

            public long GetCursor() => _cursor;
            public void RestoreCursor(long cursor) => _cursor = cursor;
            public TestResult TestConnection() => new TestResult(true, "ok");
            public void Dispose() { }

            public async IAsyncEnumerable<ISourceRecord> ReadNew(DateTime lower_date)
            {
                LowerDates.Add(lower_date);
                foreach (var row in _rows.Where(r => r.id > _cursor).ToList())
                {
                    _cursor = row.id;
                    yield return row;
                }
                await Task.CompletedTask;
            }
        }

        [Fact]
        public async Task TestPostTimeoutRetriesSameBatch()
        {
            var rows = new[] { (1, date_1), (2, date_2), (3, date_3) }
                .Select(r => new SmartDispatchPlusV1Record
                {
                    id = r.Item1,
                    recvgpstime = r.Item2,
                    happentime = r.Item2,
                    device_alias = "test",
                    deviceguid = "test",
                    gpscontext = "test",
                    streetname = "test",
                    description = "test"
                })
                .ToList();
            var reader = new CursorAdvancingReader(rows);

            var postedIds = new List<List<long>>();
            var writer_mocker = new Mock<IDataWriter>();
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Returns<IReadOnlyList<ISourceRecord>, CancellationToken>((records, ct) =>
                {
                    postedIds.Add(records.Cast<SmartDispatchPlusV1Record>().Select(r => (long)r.id).ToList());
                    if (postedIds.Count == 1)
                        throw new PostTimeoutException("timed out");
                    return Task.FromResult(0);
                });

            // 100ms interval keeps the retry backoff short.
            RadioDataPump pump = new RadioDataPump(100, 25);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(1500);

            await pump.Run(reader, writer_mocker.Object, tokenSource.Token);

            // The timed-out batch is re-sent in full, exactly once more.
            Assert.Equal(2, postedIds.Count);
            Assert.Equal(new long[] { 1, 2, 3 }, postedIds[0]);
            Assert.Equal(new long[] { 1, 2, 3 }, postedIds[1]);

            // The retry read used the same lower_date as the failed one.
            Assert.True(reader.LowerDates.Count >= 2);
            Assert.Equal(reader.LowerDates[0], reader.LowerDates[1]);
        }
    }
}
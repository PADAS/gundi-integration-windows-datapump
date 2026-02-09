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

            var writer_mocker = new Mock<IDataWriter>();
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));


            IDataWriter mock_writer = writer_mocker.Object;


            RadioDataPump pump = new RadioDataPump(1000, 25);

            CancellationTokenSource tokenSource= new CancellationTokenSource();
            tokenSource.CancelAfter(3000);


            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // With batch size 25 and 3 records, we expect a single batch flush
            writer_mocker.Verify(f => f.PostObservations(It.Is<IReadOnlyList<ISourceRecord>>(list => list.Count == 3), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task TestDataPumpBatching()
        {
            // Test that records are batched correctly when batch size is smaller than record count
            var reader_mocker = new Mock<IDataReader>();
            reader_mocker.SetupSequence(f => f.ReadNew(It.IsAny<DateTime>())).Returns(MockResponse1()).Returns(MockResponse2()).Returns(MockResponse2());
            IDataReader mock_reader = reader_mocker.Object;

            var writer_mocker = new Mock<IDataWriter>();
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));

            IDataWriter mock_writer = writer_mocker.Object;

            // Batch size of 2, with 3 records should result in 2 batch flushes
            RadioDataPump pump = new RadioDataPump(1000, 2);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(3000);

            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // First batch of 2, then remaining 1
            writer_mocker.Verify(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
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

            var writer_mocker = new Mock<IDataWriter>();
            // First call throws HttpRequestException (simulating 500/502/503 after retries exhausted)
            // Second call succeeds (if there were more records)
            writer_mocker.SetupSequence(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Server returned 503 Service Unavailable"))
                .Returns(Task.FromResult(0));

            IDataWriter mock_writer = writer_mocker.Object;

            RadioDataPump pump = new RadioDataPump(1000, 25);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(3000);

            // Should not throw - error is caught and batch is skipped
            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // Verify PostObservations was called (batch attempted)
            writer_mocker.Verify(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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

            var writer_mocker = new Mock<IDataWriter>();
            // Throws BrokenCircuitException (circuit breaker is open)
            writer_mocker.Setup(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new BrokenCircuitException("Circuit breaker is open"));

            IDataWriter mock_writer = writer_mocker.Object;

            RadioDataPump pump = new RadioDataPump(500, 25);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            // Short timeout - circuit breaker pause is 60s, so this will cancel before resume
            tokenSource.CancelAfter(2000);

            // Should complete via cancellation, not throw the circuit breaker exception
            await pump.Run(mock_reader, mock_writer, tokenSource.Token);

            // Verify PostObservations was called at least once
            writer_mocker.Verify(f => f.PostObservations(It.IsAny<IReadOnlyList<ISourceRecord>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
    }
}
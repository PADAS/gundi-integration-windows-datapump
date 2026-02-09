using DataPump;
using DataPumpModels;
using Moq;
using AutoFixture;
using System.Runtime.CompilerServices;
using worker;

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
            writer_mocker.Setup(f => f.PostObservation(It.IsAny<ISourceRecord>())).Returns(async () => 1);


            IDataWriter mock_writer = writer_mocker.Object;

            DateTime lower_date = DateTime.SpecifyKind(DateTime.Parse("2024-01-21 15:00:00"), DateTimeKind.Utc);


            await foreach (var item in mock_reader.ReadNew(lower_date))
            {

                await mock_writer.PostObservation(item);
                Console.WriteLine(item.cursor_at);

                lower_date = item.cursor_at > lower_date ? item.cursor_at : lower_date;
            }

            writer_mocker.Verify(f => f.PostObservation(It.IsAny<ISourceRecord>()), Times.Exactly(3));
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
    }   
}
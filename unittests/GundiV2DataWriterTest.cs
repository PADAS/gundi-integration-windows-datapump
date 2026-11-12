namespace general_tests
{
    public class GundiV2DataWriterTest
    {
        [Theory]
        [InlineData("https://sensors.api.gundiservice.org", "https://sensors.api.gundiservice.org")]
        [InlineData("https://sensors.api.gundiservice.org/", "https://sensors.api.gundiservice.org")]
        [InlineData("https://sensors.api.gundiservice.org/v2/observations/", "https://sensors.api.gundiservice.org")]
        [InlineData("https://sensors.api.gundiservice.org/v2/observations", "https://sensors.api.gundiservice.org")]
        [InlineData("https://sensors.api.gundiservice.org/V2/Observations/", "https://sensors.api.gundiservice.org")]
        [InlineData("  https://sensors.api.gundiservice.org/v2/observations//  ", "https://sensors.api.gundiservice.org")]
        [InlineData("https://example.org/api/v2/observations/", "https://example.org/api")]
        [InlineData("https://example.org/v2/observationsx", "https://example.org/v2/observationsx")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void NormalizeDestination_StripsObservationsPathAndSlashes(string? input, string expected)
        {
            Assert.Equal(expected, GundiV2DataWriter.NormalizeDestination(input));
        }
    }
}

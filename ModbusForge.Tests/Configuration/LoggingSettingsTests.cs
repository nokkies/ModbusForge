using Xunit;
using ModbusForge.Configuration;

namespace ModbusForge.Tests.Configuration
{
    public class LoggingSettingsTests
    {
        [Theory]
        [InlineData(0, 1)]      // Below minimum
        [InlineData(-10, 1)]    // Well below minimum
        [InlineData(1, 1)]      // Minimum
        [InlineData(30, 30)]    // Within range
        [InlineData(60, 60)]    // Maximum
        [InlineData(61, 60)]    // Above maximum
        [InlineData(100, 60)]   // Well above maximum
        public void Clamp_RetentionMinutes_ShouldBeClampedToValidRange(int input, int expected)
        {
            // Arrange
            var settings = new LoggingSettings
            {
                RetentionMinutes = input
            };

            // Act
            settings.Clamp();

            // Assert
            Assert.Equal(expected, settings.RetentionMinutes);
        }

        [Theory]
        [InlineData(0, 50)]         // Below minimum
        [InlineData(-100, 50)]      // Well below minimum
        [InlineData(50, 50)]        // Minimum
        [InlineData(1000, 1000)]    // Within range
        [InlineData(60000, 60000)]  // Maximum
        [InlineData(60001, 60000)]  // Above maximum
        [InlineData(100000, 60000)] // Well above maximum
        public void Clamp_SampleRateMs_ShouldBeClampedToValidRange(int input, int expected)
        {
            // Arrange
            var settings = new LoggingSettings
            {
                SampleRateMs = input
            };

            // Act
            settings.Clamp();

            // Assert
            Assert.Equal(expected, settings.SampleRateMs);
        }
    }
}

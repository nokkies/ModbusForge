using Xunit;
using ModbusForge.ViewModels.Coordinators;

namespace ModbusForge.Tests.Coordinators
{
    /// <summary>
    /// Basic tests for coordinator functionality that don't require complex mocking.
    /// These tests verify the coordinator pattern is working correctly.
    /// </summary>
    public class CoordinatorTestsBasic
    {
        [Fact]
        public void TrendCoordinator_GetTrendKey_ReturnsCorrectFormat()
        {
            // Arrange
            var entry = new ModbusForge.Models.CustomEntry
            {
                Area = "HoldingRegister",
                Address = 100
            };

            // Act
            var key = TrendCoordinator.GetTrendKey(entry);

            // Assert
            Assert.Equal("HoldingRegister:100", key);
        }

        [Fact]
        public void TrendCoordinator_GetTrendKey_HandlesNullArea()
        {
            // Arrange
            var entry = new ModbusForge.Models.CustomEntry
            {
                Area = null,
                Address = 50
            };

            // Act
            var key = TrendCoordinator.GetTrendKey(entry);

            // Assert
            Assert.Equal("HoldingRegister:50", key);
        }

        [Fact]
        public void TrendCoordinator_GetTrendDisplayName_UsesNameWhenProvided()
        {
            // Arrange
            var entry = new ModbusForge.Models.CustomEntry
            {
                Name = "Temperature Sensor",
                Area = "HoldingRegister",
                Address = 100,
                Type = "real"
            };

            // Act
            var displayName = TrendCoordinator.GetTrendDisplayName(entry);

            // Assert
            Assert.Equal("Temperature Sensor", displayName);
        }

        [Fact]
        public void TrendCoordinator_GetTrendDisplayName_GeneratesNameWhenEmpty()
        {
            // Arrange
            var entry = new ModbusForge.Models.CustomEntry
            {
                Name = "",
                Area = "HoldingRegister",
                Address = 100,
                Type = "uint"
            };

            // Act
            var displayName = TrendCoordinator.GetTrendDisplayName(entry);

            // Assert
            Assert.Equal("HoldingRegister 100 (uint)", displayName);
        }

        [Fact]
        public void TrendCoordinator_GetTrendDisplayName_HandlesNullName()
        {
            // Arrange
            var entry = new ModbusForge.Models.CustomEntry
            {
                Name = null,
                Area = "InputRegister",
                Address = 200,
                Type = "int"
            };

            // Act
            var displayName = TrendCoordinator.GetTrendDisplayName(entry);

            // Assert
            Assert.Equal("InputRegister 200 (int)", displayName);
        }
    }
}

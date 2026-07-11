using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Coordinators
{
    public class CustomEntryCoordinatorTests
    {
        private static CustomEntryCoordinator CreateCoordinator(
            out Mock<ModbusTcpService> clientService,
            out Mock<ModbusServerService> serverService)
        {
            clientService = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            serverService = new Mock<ModbusServerService>(new Mock<ILogger<ModbusServerService>>().Object);
            var consoleLogger = new Mock<IConsoleLoggerService>();
            var registerCoordinator = new RegisterCoordinator(
                clientService.Object,
                serverService.Object,
                consoleLogger.Object,
                new Mock<ILogger<RegisterCoordinator>>().Object);
            var customEntryService = new Mock<ICustomEntryService>();

            return new CustomEntryCoordinator(
                registerCoordinator,
                customEntryService.Object,
                clientService.Object,
                serverService.Object,
                new Mock<ILogger<CustomEntryCoordinator>>().Object);
        }

        [Fact]
        public async Task WriteCustomNowAsync_InvalidUint_ReturnsTypeConversionError()
        {
            // Arrange
            var coordinator = CreateCoordinator(out _, out _);
            var entry = new CustomEntry { Address = 1, Area = "HoldingRegister", Type = "uint", Value = "abc" };

            // Act
            var result = await coordinator.WriteCustomNowAsync(entry, 1, false);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Invalid uint", result.Message);
        }

        [Fact]
        public async Task WriteCustomNowAsync_InvalidFloat_ReturnsTypeConversionError()
        {
            // Arrange
            var coordinator = CreateCoordinator(out _, out _);
            var entry = new CustomEntry { Address = 2, Area = "HoldingRegister", Type = "real", Value = "not-a-float" };

            // Act
            var result = await coordinator.WriteCustomNowAsync(entry, 1, false);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Invalid float", result.Message);
        }

        [Fact]
        public async Task WriteCustomNowAsync_ValidUint_DelegatesToRegisterCoordinator()
        {
            // Arrange
            var coordinator = CreateCoordinator(out var clientService, out _);
            var entry = new CustomEntry { Address = 3, Area = "HoldingRegister", Type = "uint", Value = "12345" };

            // Act
            var result = await coordinator.WriteCustomNowAsync(entry, 1, false);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Wrote UINT 12345", result.Message);
            clientService.Verify(s => s.WriteSingleRegisterAsync(1, 3, 12345), Times.Once);
        }

        [Fact]
        public async Task WriteCustomNowAsync_ValidReal_DelegatesToRegisterCoordinator()
        {
            // Arrange
            var coordinator = CreateCoordinator(out var clientService, out _);
            var entry = new CustomEntry { Address = 4, Area = "HoldingRegister", Type = "real", Value = "3.14" };

            // Act
            var result = await coordinator.WriteCustomNowAsync(entry, 1, false);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("Wrote REAL 3.14", result.Message);
            clientService.Verify(s => s.WriteRegistersAsync(1, 4, It.IsAny<ushort[]>()), Times.Once);
        }

        [Fact]
        public async Task WriteCustomNowAsync_InvalidCoilValue_ReturnsTypeConversionError()
        {
            // Arrange
            var coordinator = CreateCoordinator(out _, out _);
            var entry = new CustomEntry { Address = 5, Area = "Coil", Type = "bool", Value = "maybe" };

            // Act
            var result = await coordinator.WriteCustomNowAsync(entry, 1, false);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Invalid coil value", result.Message);
        }

        [Fact]
        public async Task ReadCustomNowAsync_HoldingRegisterUint_ReturnsValueAndMessage()
        {
            // Arrange
            var coordinator = CreateCoordinator(out var clientService, out _);
            clientService.Setup(s => s.ReadHoldingRegistersAsync(1, 10, 1))
                .ReturnsAsync(new ushort[] { 999 });
            var entry = new CustomEntry { Address = 10, Area = "HoldingRegister", Type = "uint", Value = "0" };

            // Act
            var result = await coordinator.ReadCustomNowAsync(entry, 1, false);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("999", entry.Value);
            Assert.Contains("Read UINT 999 from HR 10", result.Message);
        }

        [Fact]
        public async Task ReadCustomNowAsync_HoldingRegisterReal_ReturnsValueAndMessage()
        {
            // Arrange
            var coordinator = CreateCoordinator(out var clientService, out _);
            var registers = DataTypeConverter.ToUInt16(2.5f);
            clientService.Setup(s => s.ReadHoldingRegistersAsync(1, 11, 2))
                .ReturnsAsync(registers);
            var entry = new CustomEntry { Address = 11, Area = "HoldingRegister", Type = "real", Value = "0" };

            // Act
            var result = await coordinator.ReadCustomNowAsync(entry, 1, false);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2.5f.ToString(CultureInfo.InvariantCulture), entry.Value);
            Assert.Contains("Read REAL", result.Message);
        }

        [Fact]
        public async Task ReadCustomNowAsync_InputRegisterInt_ReturnsSignedValueAndMessage()
        {
            // Arrange
            var coordinator = CreateCoordinator(out var clientService, out _);
            clientService.Setup(s => s.ReadInputRegistersAsync(1, 20, 1))
                .ReturnsAsync(new ushort[] { unchecked((ushort)-7) });
            var entry = new CustomEntry { Address = 20, Area = "InputRegister", Type = "int", Value = "0" };

            // Act
            var result = await coordinator.ReadCustomNowAsync(entry, 1, false);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("-7", entry.Value);
            Assert.Contains("Read INT -7 from IR 20", result.Message);
        }
    }
}

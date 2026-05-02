using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Performance
{
    public class WriteOptimizationTests
    {
        [Fact]
        public async Task WriteFloatAtAsync_Optimized_UsesSingleCall()
        {
            // Arrange
            var mockClientService = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            var mockServerService = new Mock<ModbusServerService>(new Mock<ILogger<ModbusServerService>>().Object);
            var mockConsoleLogger = new Mock<IConsoleLoggerService>();
            var mockCoordLogger = new Mock<ILogger<RegisterCoordinator>>();

            var coordinator = new RegisterCoordinator(
                mockClientService.Object,
                mockServerService.Object,
                mockConsoleLogger.Object,
                mockCoordLogger.Object);

            byte unitId = 1;
            int address = 100;
            float value = 123.45f;

            // Act
            await coordinator.WriteFloatAtAsync(unitId, address, value, false);

            // Assert
            // It should call WriteRegistersAsync exactly once
            mockClientService.Verify(s => s.WriteRegistersAsync(unitId, address, It.Is<ushort[]>(v => v.Length == 2)), Times.Once);
            // It should NOT call WriteSingleRegisterAsync anymore
            mockClientService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        }

        [Fact]
        public async Task WriteStringAtAsync_Optimized_UsesSingleCall()
        {
            // Arrange
            var mockClientService = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            var mockServerService = new Mock<ModbusServerService>(new Mock<ILogger<ModbusServerService>>().Object);
            var mockConsoleLogger = new Mock<IConsoleLoggerService>();
            var mockCoordLogger = new Mock<ILogger<RegisterCoordinator>>();

            var coordinator = new RegisterCoordinator(
                mockClientService.Object,
                mockServerService.Object,
                mockConsoleLogger.Object,
                mockCoordLogger.Object);

            byte unitId = 1;
            int address = 100;
            string value = "TEST"; // 4 chars = 2 registers

            // Act
            await coordinator.WriteStringAtAsync(unitId, address, value, false);

            // Assert
            // It should call WriteRegistersAsync exactly once
            mockClientService.Verify(s => s.WriteRegistersAsync(unitId, address, It.Is<ushort[]>(v => v.Length == 2)), Times.Once);
            // It should NOT call WriteSingleRegisterAsync anymore
            mockClientService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        }
    }
}

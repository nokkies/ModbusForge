using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels.Coordinators;
using Xunit;

namespace ModbusForge.Tests.Coordinators
{
    public class TrendCoordinatorTests
    {
        private readonly Mock<IModbusService> _clientServiceMock;
        private readonly Mock<IModbusService> _serverServiceMock;
        private readonly Mock<ITrendLogger> _trendLoggerMock;
        private readonly Mock<ILogger<TrendCoordinator>> _loggerMock;
        private readonly TrendCoordinator _coordinator;

        public TrendCoordinatorTests()
        {
            _clientServiceMock = new Mock<IModbusService>();
            _serverServiceMock = new Mock<IModbusService>();
            _trendLoggerMock = new Mock<ITrendLogger>();
            _loggerMock = new Mock<ILogger<TrendCoordinator>>();

            var settingsServiceMock = new Mock<ISettingsService>();
            settingsServiceMock.Setup(s => s.MaxConcurrentTrendRequests).Returns(8);

            _coordinator = new TrendCoordinator(
                _clientServiceMock.Object,
                _serverServiceMock.Object,
                _trendLoggerMock.Object,
                _loggerMock.Object,
                settingsServiceMock.Object);
        }

        [Fact]
        public async Task ProcessTrendSamplingAsync_ShouldGroupContiguousRequests()
        {
            // Arrange
            var entries = new List<CustomEntry>();
            for (int i = 0; i < 10; i++)
            {
                entries.Add(new CustomEntry
                {
                    Name = $"Entry{i}",
                    Address = i + 1, // 1-based address: 1, 2, ..., 10
                    Type = "uint",
                    Area = "HoldingRegister",
                    Trend = true
                });
            }

            // Setup mock to return an array of 10 ushorts (for addresses 1 to 10)
            ushort[] returnData = Enumerable.Range(1, 10).Select(i => (ushort)i).ToArray();

            _clientServiceMock
                .Setup(x => x.ReadHoldingRegistersAsync(1, 1, 10))
                .ReturnsAsync(returnData);

            bool monitorEnabled = true;

            // Act
            await _coordinator.ProcessTrendSamplingAsync(
                entries,
                unitId: 1,
                isServerMode: false,
                setGlobalMonitorEnabled: val => monitorEnabled = val);

            // Assert
            // Should call ReadHoldingRegistersAsync EXACTLY ONCE with start=1, count=10
            _clientServiceMock.Verify(
                x => x.ReadHoldingRegistersAsync(1, 1, 10),
                Times.Once,
                "Should group 10 contiguous registers into a single read request");

            // Also verify that individual values were updated and published
            // Entry 1 (address 1) should have value 1
            // Entry 10 (address 10) should have value 10
            _trendLoggerMock.Verify(x => x.Publish("HoldingRegister:1", 1.0, It.IsAny<DateTime>()), Times.Once);
            _trendLoggerMock.Verify(x => x.Publish("HoldingRegister:10", 10.0, It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task ProcessTrendSamplingAsync_ShouldSplitRequestsWithLargeGap()
        {
            // Arrange
            var entries = new List<CustomEntry>
            {
                new CustomEntry { Address = 1, Type = "uint", Area = "HoldingRegister", Trend = true },
                new CustomEntry { Address = 100, Type = "uint", Area = "HoldingRegister", Trend = true } // Gap > 10
            };

            _clientServiceMock
                .Setup(x => x.ReadHoldingRegistersAsync(1, 1, 1))
                .ReturnsAsync(new ushort[] { 1 });

            _clientServiceMock
                .Setup(x => x.ReadHoldingRegistersAsync(1, 100, 1))
                .ReturnsAsync(new ushort[] { 100 });

            // Act
            await _coordinator.ProcessTrendSamplingAsync(
                entries,
                unitId: 1,
                isServerMode: false,
                setGlobalMonitorEnabled: _ => { });

            // Assert
            _clientServiceMock.Verify(x => x.ReadHoldingRegistersAsync(1, 1, 1), Times.Once);
            _clientServiceMock.Verify(x => x.ReadHoldingRegistersAsync(1, 100, 1), Times.Once);
            _clientServiceMock.Verify(x => x.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ProcessTrendSamplingAsync_ShouldHandleMixedTypesInChunk()
        {
             // Arrange
            var entries = new List<CustomEntry>
            {
                new CustomEntry { Address = 10, Type = "uint", Area = "HoldingRegister", Trend = true }, // Size 1
                new CustomEntry { Address = 11, Type = "real", Area = "HoldingRegister", Trend = true }  // Size 2 (11, 12)
            };
            // Total range: 10 to 12. Count = 3.

            // Mock return data:
            // 10: 55
            // 11-12: float 123.45 -> (low, high) or (high, low).
            // DataTypeConverter.ToSingle(u1, u2). Assuming implementation uses (u1 | u2<<16) or similar.
            // Let's rely on mapping.

            _clientServiceMock
                .Setup(x => x.ReadHoldingRegistersAsync(1, 10, 3))
                .ReturnsAsync(new ushort[] { 55, 0, 0 }); // Just dummy data

            // Act
             await _coordinator.ProcessTrendSamplingAsync(
                entries,
                unitId: 1,
                isServerMode: false,
                setGlobalMonitorEnabled: _ => { });

             // Assert
             _clientServiceMock.Verify(x => x.ReadHoldingRegistersAsync(1, 10, 3), Times.Once);
        }
    }
}

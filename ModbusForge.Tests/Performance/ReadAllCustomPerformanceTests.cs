using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ModbusForge.Configuration;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using ModbusForge.ViewModels.Coordinators;

namespace ModbusForge.Tests.Performance
{
    public class ReadAllCustomPerformanceTests
    {
        private readonly Mock<ModbusTcpService> _mockClientService;
        private readonly Mock<ModbusServerService> _mockServerService;
        private readonly Mock<ILogger<MainViewModel>> _mockLogger;
        private readonly Mock<IOptions<ServerSettings>> _mockOptions;
        private readonly Mock<ITrendLogger> _mockTrendLogger;
        private readonly Mock<ICustomEntryService> _mockCustomEntryService;
        private readonly Mock<IConsoleLoggerService> _mockConsoleLogger;

        public ReadAllCustomPerformanceTests()
        {
            _mockLogger = new Mock<ILogger<MainViewModel>>();
            _mockOptions = new Mock<IOptions<ServerSettings>>();
            _mockOptions.Setup(o => o.Value).Returns(new ServerSettings());
            _mockTrendLogger = new Mock<ITrendLogger>();
            _mockCustomEntryService = new Mock<ICustomEntryService>();
            _mockConsoleLogger = new Mock<IConsoleLoggerService>();

            var clientLogger = new Mock<ILogger<ModbusTcpService>>();
            _mockClientService = new Mock<ModbusTcpService>(clientLogger.Object);

            var serverLogger = new Mock<ILogger<ModbusServerService>>();
            _mockServerService = new Mock<ModbusServerService>(serverLogger.Object);
        }

        [Fact]
        public async Task Baseline_ReadAllCustom_Sequential_Performance()
        {
            // Arrange
            const int entryCount = 10;
            const int delayMs = 50;

            // Mock holding register reads with a delay
            _mockClientService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async () =>
                {
                    await Task.Delay(delayMs);
                    return new ushort[] { 123 };
                });
            _mockClientService.SetupGet(s => s.IsConnected).Returns(true);

            var connectionCoordinator = new ConnectionCoordinator(_mockClientService.Object, _mockServerService.Object, _mockConsoleLogger.Object, new Mock<ILogger<ConnectionCoordinator>>().Object);
            var registerCoordinator = new RegisterCoordinator(_mockClientService.Object, _mockServerService.Object, _mockConsoleLogger.Object, new Mock<ILogger<RegisterCoordinator>>().Object);
            var customEntryCoordinator = new CustomEntryCoordinator(registerCoordinator, _mockCustomEntryService.Object, _mockClientService.Object, _mockServerService.Object, new Mock<ILogger<CustomEntryCoordinator>>().Object);
            var trendCoordinator = new TrendCoordinator(_mockClientService.Object, _mockServerService.Object, _mockTrendLogger.Object, new Mock<ILogger<TrendCoordinator>>().Object);
            var configurationCoordinator = new ConfigurationCoordinator(new Mock<ILogger<ConfigurationCoordinator>>().Object);
            var simulationCoordinator = new SimulationCoordinator(new Mock<ISimulationService>().Object);

            var viewModel = new MainViewModel(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockLogger.Object,
                _mockOptions.Object,
                _mockTrendLogger.Object,
                _mockCustomEntryService.Object,
                _mockConsoleLogger.Object,
                connectionCoordinator,
                registerCoordinator,
                customEntryCoordinator,
                trendCoordinator,
                configurationCoordinator,
                simulationCoordinator);

            // Add many custom entries
            for (int i = 1; i <= entryCount; i++)
            {
                viewModel.CustomEntries.Add(new CustomEntry { Address = i, Area = "HoldingRegister", Type = "uint" });
            }

            // Act
            var sw = Stopwatch.StartNew();
            // Use reflection to call the private method ReadAllCustomNowAsync
            var method = typeof(MainViewModel).GetMethod("ReadAllCustomNowAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)method.Invoke(viewModel, null);
            sw.Stop();

            // Assert
            Console.WriteLine($"Sequential read of {entryCount} entries took {sw.ElapsedMilliseconds}ms");
            // Expected time: ~10 * 50ms = 500ms
            Assert.True(sw.ElapsedMilliseconds >= entryCount * delayMs, $"Expected at least {entryCount * delayMs}ms but took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Optimized_ReadAllCustom_Batched_Performance()
        {
            // Arrange
            const int entryCount = 10;
            const int delayMs = 50;

            // Mock holding register reads with a delay
            // In batched mode, it should only call ReadHoldingRegistersAsync ONCE for contiguous addresses
            _mockClientService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (byte uid, int addr, int count) =>
                {
                    await Task.Delay(delayMs);
                    return new ushort[count];
                });
            _mockClientService.SetupGet(s => s.IsConnected).Returns(true);

            var connectionCoordinator = new ConnectionCoordinator(_mockClientService.Object, _mockServerService.Object, _mockConsoleLogger.Object, new Mock<ILogger<ConnectionCoordinator>>().Object);
            var registerCoordinator = new RegisterCoordinator(_mockClientService.Object, _mockServerService.Object, _mockConsoleLogger.Object, new Mock<ILogger<RegisterCoordinator>>().Object);
            var customEntryCoordinator = new CustomEntryCoordinator(registerCoordinator, _mockCustomEntryService.Object, _mockClientService.Object, _mockServerService.Object, new Mock<ILogger<CustomEntryCoordinator>>().Object);
            var trendCoordinator = new TrendCoordinator(_mockClientService.Object, _mockServerService.Object, _mockTrendLogger.Object, new Mock<ILogger<TrendCoordinator>>().Object);
            var configurationCoordinator = new ConfigurationCoordinator(new Mock<ILogger<ConfigurationCoordinator>>().Object);
            var simulationCoordinator = new SimulationCoordinator(new Mock<ISimulationService>().Object);

            var viewModel = new MainViewModel(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockLogger.Object,
                _mockOptions.Object,
                _mockTrendLogger.Object,
                _mockCustomEntryService.Object,
                _mockConsoleLogger.Object,
                connectionCoordinator,
                registerCoordinator,
                customEntryCoordinator,
                trendCoordinator,
                configurationCoordinator,
                simulationCoordinator);

            // Add contiguous custom entries (Address 1 to 10)
            for (int i = 1; i <= entryCount; i++)
            {
                viewModel.CustomEntries.Add(new CustomEntry { Address = i, Area = "HoldingRegister", Type = "uint" });
            }

            // Act
            var sw = Stopwatch.StartNew();
            var method = typeof(MainViewModel).GetMethod("ReadAllCustomNowAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            await (Task)method.Invoke(viewModel, null);
            sw.Stop();

            // Assert
            Console.WriteLine($"Optimized read of {entryCount} entries took {sw.ElapsedMilliseconds}ms");
            // Expected time: ~1 * 50ms = 50ms (instead of 500ms)
            // We allow some overhead, so check if it's significantly faster than sequential
            Assert.True(sw.ElapsedMilliseconds < (entryCount * delayMs) / 2, $"Expected significant speedup. Sequential would take {entryCount * delayMs}ms, but took {sw.ElapsedMilliseconds}ms");

            // Verify that ReadHoldingRegistersAsync was called fewer times (should be 1 for 10 contiguous registers)
            _mockClientService.Verify(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.AtMost(2));
        }
    }
}

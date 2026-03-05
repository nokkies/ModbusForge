using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ModbusForge.Configuration;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using ModbusForge.ViewModels.Coordinators;

namespace ModbusForge.Tests.Performance
{
    public class BlockingModeSwitchTests
    {
        private Mock<ModbusTcpService> _mockClientService;
        private Mock<ModbusServerService> _mockServerService;
        private Mock<ILogger<MainViewModel>> _mockLogger;
        private Mock<IOptions<ServerSettings>> _mockOptions;
        private Mock<ITrendLogger> _mockTrendLogger;
        private Mock<ISimulationService> _mockSimulationService;
        private Mock<ICustomEntryService> _mockCustomEntryService;
        private Mock<IConsoleLoggerService> _mockConsoleLogger;

        // Coordinators
        private ConnectionCoordinator _connectionCoordinator;
        private RegisterCoordinator _registerCoordinator;
        private CustomEntryCoordinator _customEntryCoordinator;
        private TrendCoordinator _trendCoordinator;
        private ConfigurationCoordinator _configurationCoordinator;
        private SimulationCoordinator _simulationCoordinator;

        public BlockingModeSwitchTests()
        {
            _mockLogger = new Mock<ILogger<MainViewModel>>();

            // Mock Options
            _mockOptions = new Mock<IOptions<ServerSettings>>();
            _mockOptions.Setup(o => o.Value).Returns(new ServerSettings());

            _mockTrendLogger = new Mock<ITrendLogger>();
            _mockSimulationService = new Mock<ISimulationService>();
            _mockCustomEntryService = new Mock<ICustomEntryService>();
            _mockConsoleLogger = new Mock<IConsoleLoggerService>();

            // Mock Services
            // We use MockBehavior.Loose so unsetup methods don't throw
            // ModbusTcpService requires ILogger<ModbusTcpService> in constructor
            var clientLogger = new Mock<ILogger<ModbusTcpService>>();
            _mockClientService = new Mock<ModbusTcpService>(clientLogger.Object);

            // ModbusServerService requires ILogger<ModbusServerService> in constructor
            var serverLogger = new Mock<ILogger<ModbusServerService>>();
            _mockServerService = new Mock<ModbusServerService>(serverLogger.Object);

            // Construct Coordinators with mocks
            _connectionCoordinator = new ConnectionCoordinator(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockConsoleLogger.Object,
                new Mock<ILogger<ConnectionCoordinator>>().Object);

            _registerCoordinator = new RegisterCoordinator(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockConsoleLogger.Object,
                new Mock<ILogger<RegisterCoordinator>>().Object);

            _customEntryCoordinator = new CustomEntryCoordinator(
                _registerCoordinator,
                _mockCustomEntryService.Object,
                _mockClientService.Object,
                _mockServerService.Object,
                new Mock<ILogger<CustomEntryCoordinator>>().Object);

            _trendCoordinator = new TrendCoordinator(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockTrendLogger.Object,
                new Mock<ILogger<TrendCoordinator>>().Object);

            _configurationCoordinator = new ConfigurationCoordinator(
                new Mock<ILogger<ConfigurationCoordinator>>().Object);

            _simulationCoordinator = new SimulationCoordinator(_mockSimulationService.Object);
        }

        [Fact]
        public async Task ModeSwitch_ShouldNotBlockUI_WhenDisconnecting()
        {
            // Arrange
            // Simulate a slow disconnect on the Client service
            _mockClientService.Setup(s => s.DisconnectAsync())
                .Returns(async () => await Task.Delay(1000));

            _mockClientService.SetupGet(s => s.IsConnected).Returns(true);

            var viewModel = new MainViewModel(
                _mockClientService.Object,
                _mockServerService.Object,
                _mockLogger.Object,
                _mockOptions.Object,
                _mockTrendLogger.Object,
                _mockCustomEntryService.Object,
                _mockConsoleLogger.Object,
                _connectionCoordinator,
                _registerCoordinator,
                _customEntryCoordinator,
                _trendCoordinator,
                _configurationCoordinator,
                _simulationCoordinator);

            // Set initial state
            viewModel.Mode = "Client";
            // Force IsConnected to true (it reads from _modbusService.IsConnected which is mocked to true)

            Assert.True(viewModel.IsConnected, "ViewModel should be connected initially");
            Assert.Equal("Client", viewModel.Mode);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Switching mode should trigger DisconnectAsync on the current service (Client)
            viewModel.Mode = "Server";

            stopwatch.Stop();

            // Assert
            // If it blocks, it will take > 1000ms. If optimized, it should be instant (< 200ms).
            Assert.True(stopwatch.ElapsedMilliseconds < 500,
                $"Mode switch took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms. The UI thread is blocked!");

            Assert.Equal("Server", viewModel.Mode);

            // Allow time for the fire-and-forget Task.Run in OnModeChanged to execute
            await Task.Delay(100);

            // We can't easily assert IsConnected state immediately if it's async,
            // but we can assert that DisconnectAsync was called.
            _mockClientService.Verify(s => s.DisconnectAsync(), Times.Once);
        }
    }
}

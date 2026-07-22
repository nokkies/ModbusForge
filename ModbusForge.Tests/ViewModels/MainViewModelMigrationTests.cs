using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class MainViewModelMigrationTests
    {
        private MainViewModel CreateViewModel()
        {
            var mockTcpService = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            var mockServerService = new Mock<ModbusServerService>(new Mock<ILogger<ModbusServerService>>().Object);
            var mockLogger = new Mock<ILogger<MainViewModel>>();
            var mockOptions = new Mock<IOptions<ServerSettings>>();
            mockOptions.Setup(o => o.Value).Returns(new ServerSettings());
            var mockTrendLogger = new Mock<ITrendLogger>();
            var mockCustomEntryService = new Mock<ICustomEntryService>();
            var mockConsoleLogger = new Mock<IConsoleLoggerService>();

            var mockRetry = new Mock<IRetryPolicyService>();
            mockRetry.Setup(r => r.ExecuteWithRetryAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (Func<Task<bool>> op, string name, int max, int init, int maxD) => await op());
            var mockValidation = new Mock<IValidationService>();
            mockValidation.Setup(v => v.ValidateIpAddress(It.IsAny<string>())).Returns(ValidationResult.Success);
            mockValidation.Setup(v => v.ValidatePort(It.IsAny<int>())).Returns(ValidationResult.Success);
            var mockError = new Mock<IErrorHandlingService>();
            mockError.Setup(e => e.HandleError(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new ErrorHandlingResult { UserMessage = "Error", RecoverySuggestion = "Suggestion" });
            var mockCircuit = new Mock<ICircuitBreakerService>();
            mockCircuit.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<CircuitBreakerConfig>()))
                .Returns(async (string name, Func<Task<bool>> op, CircuitBreakerConfig cfg) => await op());

            var connectionCoordinator = new ConnectionCoordinator(mockTcpService.Object, mockServerService.Object, mockConsoleLogger.Object, new Mock<ILogger<ConnectionCoordinator>>().Object, mockRetry.Object, mockValidation.Object, mockError.Object, mockCircuit.Object);
            var registerCoordinator = new RegisterCoordinator(mockTcpService.Object, mockServerService.Object, mockConsoleLogger.Object, new Mock<ILogger<RegisterCoordinator>>().Object);
            var customEntryCoordinator = new CustomEntryCoordinator(registerCoordinator, mockCustomEntryService.Object, mockTcpService.Object, mockServerService.Object, new Mock<ILogger<CustomEntryCoordinator>>().Object);
            var trendCoordinator = new TrendCoordinator(mockTcpService.Object, mockServerService.Object, mockTrendLogger.Object, new Mock<ILogger<TrendCoordinator>>().Object, new Mock<ISettingsService>().Object);
            var configurationCoordinator = new ConfigurationCoordinator(new Mock<ILogger<ConfigurationCoordinator>>().Object);
            var monitoringCoordinator = new MonitoringCoordinator(
                Mock.Of<IMonitoringCallbacks>(),
                Mock.Of<IPeriodicScheduler>(),
                Mock.Of<IPeriodicScheduler>(),
                Mock.Of<IPeriodicScheduler>(),
                new Mock<ILogger<MonitoringCoordinator>>().Object);

            return new MainViewModel(
                mockTcpService.Object,
                mockServerService.Object,
                mockLogger.Object,
                mockOptions.Object,
                mockTrendLogger.Object,
                mockCustomEntryService.Object,
                mockConsoleLogger.Object,
                connectionCoordinator,
                registerCoordinator,
                customEntryCoordinator,
                trendCoordinator,
                configurationCoordinator,
                monitoringCoordinator,
                new UnitConfigurationStore(new ImmediateDispatcher()),
                null,
                new VisualNodeEditorViewModel());
        }

        [Fact]
        public void MigrateOldNodeAddresses_SetsAddressZeroToOne_ForAllAreas()
        {
            // Arrange
            var vm = CreateViewModel();
            var areas = Enum.GetValues(typeof(PlcArea)).Cast<PlcArea>();

            foreach (var area in areas)
            {
                var node = new VisualNode
                {
                    Input1Address = new PlcAddressReference { Area = area, Address = 0 },
                    Input2Address = new PlcAddressReference { Area = area, Address = 0 },
                    OutputAddress = new PlcAddressReference { Area = area, Address = 0 }
                };

                // Act
                vm.VisualNodeEditorViewModel.MigrateNode(node);

                // Assert
                Assert.Equal(1, node.Input1Address.Address);
                Assert.Equal(1, node.Input2Address.Address);
                Assert.Equal(1, node.OutputAddress.Address);
            }
        }

        [Fact]
        public void MigrateOldNodeAddresses_FixesMissingOutputAddress_ForInputInt()
        {
            // Arrange
            var vm = CreateViewModel();
            var node = new VisualNode
            {
                ElementType = PlcElementType.InputInt,
                Input1Address = new PlcAddressReference { Area = PlcArea.HoldingRegister, Address = 5 },
                OutputAddress = null!
            };

            // Act
            vm.VisualNodeEditorViewModel.MigrateNode(node);

            // Assert
            Assert.NotNull(node.OutputAddress);
            Assert.Equal(PlcArea.HoldingRegister, node.OutputAddress.Area);
            Assert.Equal(5, node.OutputAddress.Address);
        }

        [Fact]
        public void MigrateOldNodeAddresses_FixesMissingOutputAddress_ForInputBool()
        {
            // Arrange
            var vm = CreateViewModel();
            var node = new VisualNode
            {
                ElementType = PlcElementType.InputBool,
                Input1Address = new PlcAddressReference { Area = PlcArea.Coil, Address = 10 },
                OutputAddress = null!
            };

            // Act
            vm.VisualNodeEditorViewModel.MigrateNode(node);

            // Assert
            Assert.NotNull(node.OutputAddress);
            Assert.Equal(PlcArea.Coil, node.OutputAddress.Area);
            Assert.Equal(10, node.OutputAddress.Address);
        }

        [Fact]
        public void MigrateOldNodeAddresses_MigratesConnectorConfigs()
        {
            // Arrange
            var vm = CreateViewModel();
            var nodeId = "test-node-1";
            var node = new VisualNode { Id = nodeId };

            var config1 = new ConnectorConfiguration { NodeId = nodeId, Address = 0 };
            var config2 = new ConnectorConfiguration { NodeId = nodeId, Address = 5 };
            var config3 = new ConnectorConfiguration { NodeId = "other-node", Address = 0 };

            vm.VisualNodeEditorViewModel.ConnectorConfigs.Add(config1);
            vm.VisualNodeEditorViewModel.ConnectorConfigs.Add(config2);
            vm.VisualNodeEditorViewModel.ConnectorConfigs.Add(config3);

            // Act
            vm.VisualNodeEditorViewModel.MigrateNode(node);

            // Assert
            Assert.Equal(1, config1.Address);
            Assert.Equal(5, config2.Address);
            Assert.Equal(0, config3.Address); // Should not be migrated as it belongs to another node
        }
    }
}

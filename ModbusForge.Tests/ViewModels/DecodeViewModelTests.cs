using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class DecodeViewModelTests
    {
        private Mock<ModbusTcpService> _mockTcpService = null!;
        private Mock<ModbusServerService> _mockServerService = null!;
        private Mock<ILogger<DecodeViewModel>> _mockLogger = null!;
        private Mock<MainViewModel> _mockMainViewModel = null!;

        private DecodeViewModel CreateViewModel()
        {
            _mockTcpService = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            _mockServerService = new Mock<ModbusServerService>(new Mock<ILogger<ModbusServerService>>().Object);
            _mockLogger = new Mock<ILogger<DecodeViewModel>>();

            var mockLoggerMain = new Mock<ILogger<MainViewModel>>();
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

            var connectionCoordinator = new ConnectionCoordinator(_mockTcpService.Object, _mockServerService.Object, mockConsoleLogger.Object, new Mock<ILogger<ConnectionCoordinator>>().Object, mockRetry.Object, mockValidation.Object, mockError.Object, mockCircuit.Object);
            var registerCoordinator = new RegisterCoordinator(_mockTcpService.Object, _mockServerService.Object, mockConsoleLogger.Object, new Mock<ILogger<RegisterCoordinator>>().Object);
            var customEntryCoordinator = new CustomEntryCoordinator(registerCoordinator, mockCustomEntryService.Object, _mockTcpService.Object, _mockServerService.Object, new Mock<ILogger<CustomEntryCoordinator>>().Object);
            var trendCoordinator = new TrendCoordinator(_mockTcpService.Object, _mockServerService.Object, mockTrendLogger.Object, new Mock<ILogger<TrendCoordinator>>().Object, new Mock<ISettingsService>().Object);
            var configurationCoordinator = new ConfigurationCoordinator(new Mock<ILogger<ConfigurationCoordinator>>().Object);

            _mockMainViewModel = new Mock<MainViewModel>(
                _mockTcpService.Object,
                _mockServerService.Object,
                mockLoggerMain.Object,
                mockOptions.Object,
                mockTrendLogger.Object,
                mockCustomEntryService.Object,
                mockConsoleLogger.Object,
                connectionCoordinator,
                registerCoordinator,
                customEntryCoordinator,
                trendCoordinator,
                configurationCoordinator,
                new Mock<IDialogService>().Object,
                new VisualNodeEditorViewModel(),
                new Mock<IDispatcher>().Object
            );

            // Important: Call CallBase so we don't null-ref on PropertyChanged, etc.
            _mockMainViewModel.CallBase = true;
            _mockMainViewModel.Object.IsConnected = true;
            _mockMainViewModel.Object.Mode = "Client";
            _mockMainViewModel.Object.UnitId = 1;

            return new DecodeViewModel(_mockMainViewModel.Object, _mockTcpService.Object, _mockServerService.Object, _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithValidDependencies_SetsDefaultProperties()
        {
            var vm = CreateViewModel();

            Assert.Equal("HoldingRegister", vm.Area);
            Assert.Equal(1, vm.Address);
            Assert.True(vm.UseTwoRegisters);
            Assert.Equal("1", vm.AddressInput);
            Assert.NotNull(vm.ReadNowCommand);
        }

        [Fact]
        public void ReadNowCommand_WhenNotConnected_CannotExecute()
        {
            var vm = CreateViewModel();
            _mockMainViewModel.Object.IsConnected = false;

            var canExecute = vm.ReadNowCommand.CanExecute(null);

            Assert.False(canExecute);
        }

        [Fact]
        public void ReadNowCommand_WhenConnected_CanExecute()
        {
            var vm = CreateViewModel();
            _mockMainViewModel.Object.IsConnected = true;

            var canExecute = vm.ReadNowCommand.CanExecute(null);

            Assert.True(canExecute);
        }
        [Fact]
        public async Task ReadAsync_HoldingRegister_ReadsSuccessfully()
        {
            var vm = CreateViewModel();
            vm.Area = "HoldingRegister";
            _mockTcpService.Setup(s => s.ReadHoldingRegistersAsync(1, 1, 2))
                .ReturnsAsync(new ushort[] { 0x1234, 0x5678 });

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Contains("Read 2 HR", vm.Status);
            _mockTcpService.Verify(s => s.ReadHoldingRegistersAsync(1, 1, 2), Times.Once);
        }

        [Fact]
        public async Task ReadAsync_InputRegister_ReadsSuccessfully()
        {
            var vm = CreateViewModel();
            vm.Area = "InputRegister";
            _mockTcpService.Setup(s => s.ReadInputRegistersAsync(1, 1, 2))
                .ReturnsAsync(new ushort[] { 0x1234, 0x5678 });

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Contains("Read 2 IR", vm.Status);
            _mockTcpService.Verify(s => s.ReadInputRegistersAsync(1, 1, 2), Times.Once);
        }

        [Fact]
        public async Task ReadAsync_Coil_ReadsSuccessfully()
        {
            var vm = CreateViewModel();
            vm.Area = "Coil";
            _mockTcpService.Setup(s => s.ReadCoilsAsync(1, 1, 2))
                .ReturnsAsync(new bool[] { true, false });

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Contains("Read 2 Coil", vm.Status);
            _mockTcpService.Verify(s => s.ReadCoilsAsync(1, 1, 2), Times.Once);
        }

        [Fact]
        public async Task ReadAsync_DiscreteInput_ReadsSuccessfully()
        {
            var vm = CreateViewModel();
            vm.Area = "DiscreteInput";
            _mockTcpService.Setup(s => s.ReadDiscreteInputsAsync(1, 1, 2))
                .ReturnsAsync(new bool[] { false, true });

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Contains("Read 2 DIn", vm.Status);
            _mockTcpService.Verify(s => s.ReadDiscreteInputsAsync(1, 1, 2), Times.Once);
        }
        [Fact]
        public async Task ReadAsync_WhenModbusReturnsNull_SetsStatusToNoData()
        {
            var vm = CreateViewModel();
            vm.Area = "HoldingRegister";
            _mockTcpService.Setup(s => s.ReadHoldingRegistersAsync(1, 1, 2))
                .ReturnsAsync((ushort[]?)null);

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal("No data returned", vm.Status);
        }

        [Fact]
        public async Task ReadAsync_WhenModbusThrowsException_LogsErrorAndSetsStatus()
        {
            var vm = CreateViewModel();
            vm.Area = "HoldingRegister";
            _mockTcpService.Setup(s => s.ReadHoldingRegistersAsync(1, 1, 2))
                .ThrowsAsync(new Exception("Network timeout"));

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.StartsWith("Error: Network timeout", vm.Status);
            // Verify IsBusy was reset
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public async Task ReadAsync_WithInvalidAddress_SetsStatusToInvalidAddress()
        {
            var vm = CreateViewModel();
            vm.AddressInput = "INVALID";

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.StartsWith("Invalid address", vm.Status);
        }

        [Fact]
        public async Task ReadAsync_TransitionsIsBusyStateCorrectly()
        {
            var vm = CreateViewModel();
            vm.Area = "HoldingRegister";

            var tcs = new TaskCompletionSource<ushort[]?>();
            _mockTcpService.Setup(s => s.ReadHoldingRegistersAsync(1, 1, 2))
                .Returns(tcs.Task);

            var readTask = vm.ReadNowCommand.ExecuteAsync(null);

            Assert.True(vm.IsBusy);

            tcs.SetResult(new ushort[] { 0x1234, 0x5678 });
            await readTask;

            Assert.False(vm.IsBusy);
        }
        [Fact]
        public async Task ReadAsync_ComputesDecodedVariantsCorrectly_Compute16_Compute32()
        {
            var vm = CreateViewModel();
            vm.Area = "HoldingRegister";

            // "ABCD" -> 0x4142, 0x4344
            _mockTcpService.Setup(s => s.ReadHoldingRegistersAsync(1, 1, 2))
                .ReturnsAsync(new ushort[] { 0x4142, 0x4344 });

            await vm.ReadNowCommand.ExecuteAsync(null);

            // Assert 32-bit (No swap)
            Assert.Equal("0x41424344", vm.Raw32HexNone);
            Assert.Equal("ABCD", vm.Ascii4TextNone);

            // Assert 16-bit (No swap)
            Assert.Equal("0x4142", vm.Raw16HexNone);
            Assert.Equal("AB", vm.Ascii2TextNone);

            // Swap Bytes (b[0] <-> b[1], b[2] <-> b[3]) -> 0x4241, 0x4443 -> "BADC"
            Assert.Equal("0x42414443", vm.Raw32HexSwapB);
            Assert.Equal("BADC", vm.Ascii4TextSwapB);

            // Verify a numeric compute output
            // 0x41424344 = 1094861636
            Assert.Equal("1094861636", vm.Uint32TextNone);
        }
    }
}

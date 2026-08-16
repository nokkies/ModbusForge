using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class DecodeViewModelTests
    {
        [Fact]
        public void Constructor_SetsDecodeDefaults()
        {
            var (vm, _) = CreateViewModel();

            Assert.Equal("HoldingRegister", vm.Area);
            Assert.Equal(1, vm.Address);
            Assert.Equal("1", vm.AddressInput);
            Assert.Equal(2, vm.ReadCount);
            Assert.Equal(new[] { 1, 2 }, vm.ReadCountOptions);
            Assert.NotNull(vm.ReadNowCommand);
        }

        [Theory]
        [InlineData("100")]
        [InlineData("0x64")]
        [InlineData("&H64")]
        public async Task ReadAsync_AcceptsDecimalAndHexAddresses(string addressInput)
        {
            var (vm, service) = CreateViewModel();
            vm.AddressInput = addressInput;
            service.HoldingRegistersResult = new ushort[] { 0x1234, 0x5678 };

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal(100, vm.Address);
            Assert.Equal(100, service.LastAddress);
            Assert.Equal(2, service.LastCount);
        }

        [Fact]
        public async Task ReadAsync_ComputesAllDecodeVariants()
        {
            var (vm, service) = CreateViewModel();
            service.HoldingRegistersResult = new ushort[] { 0x4142, 0x4344 };

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal("0x4142", vm.Raw16HexNone);
            Assert.Equal("AB", vm.Ascii2TextNone);
            Assert.Equal("0x42414443", vm.Raw32HexSwapB);
            Assert.Equal("BADC", vm.Ascii4TextSwapB);
            Assert.Equal("1094861636", vm.Uint32TextNone);
            Assert.Equal("Read 2 HR from 1", vm.Status);
        }

        [Theory]
        [InlineData("InputRegister", "input")]
        [InlineData("Coil", "coil")]
        [InlineData("DiscreteInput", "discrete")]
        public async Task ReadAsync_ReadsEachSupportedArea(string area, string expectedOperation)
        {
            var (vm, service) = CreateViewModel();
            vm.Area = area;

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal(expectedOperation, service.LastOperation);
            Assert.Equal((byte)7, service.LastUnitId);
            Assert.Equal(1, service.LastAddress);
            Assert.Equal(2, service.LastCount);
        }

        [Fact]
        public async Task ReadAsync_RejectsOutOfRangeAddress()
        {
            var (vm, service) = CreateViewModel();
            vm.AddressInput = "0x10000";

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.StartsWith("Invalid address", vm.Status);
            Assert.Null(service.LastOperation);
        }

        [Fact]
        public void ReadNowCommand_IsDisabledWhenServiceIsDisconnected()
        {
            var (vm, _) = CreateViewModel(connected: false);

            Assert.False(vm.ReadNowCommand.CanExecute(null));
        }

        [Fact]
        public async Task ReadAsync_CountOne_ReadsSingleRegisterAndZeroPadsTheSecondWord()
        {
            var (vm, service) = CreateViewModel();
            vm.ReadCount = 1;
            service.HoldingRegistersResult = new ushort[] { 0x4149 };

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal(1, service.LastCount);
            Assert.Equal("0x4149", vm.Raw16HexNone);
            Assert.Equal("0x41490000", vm.Raw32HexNone);
            Assert.Equal("Read 1 HR from 1", vm.Status);
        }

        [Fact]
        public async Task ReadAsync_DecomposesFloat32InAllVariants()
        {
            // 12.5f == 0x41480000. Only the None variant matches that layout.
            var (vm, service) = CreateViewModel();
            service.HoldingRegistersResult = new ushort[] { 0x4148, 0x0000 };

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal("12.5", vm.Float32TextNone);
            Assert.NotEqual("12.5", vm.Float32TextSwapB);
            Assert.NotEqual("12.5", vm.Float32TextSwapW);
            Assert.NotEqual("12.5", vm.Float32TextSwapBW);
        }

        [Fact]
        public async Task ReadAsync_SwapVariantsProduceTheFourWordOrders()
        {
            // Registers 0x4142 0x4344 -> wire bytes A B C D. The four variants must
            // yield the four classic 32-bit orders: ABCD, BADC, CDAB, DCBA.
            var (vm, service) = CreateViewModel();
            service.HoldingRegistersResult = new ushort[] { 0x4142, 0x4344 };

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.Equal("0x41424344", vm.Raw32HexNone);
            Assert.Equal("0x42414443", vm.Raw32HexSwapB);
            Assert.Equal("0x43444142", vm.Raw32HexSwapW);
            Assert.Equal("0x44434241", vm.Raw32HexSwapBW);
        }

        [Fact]
        public async Task ReadAsync_ResetsBusyStateWhenServiceFails()
        {
            var (vm, service) = CreateViewModel();
            service.Error = new InvalidOperationException("Network timeout");

            await vm.ReadNowCommand.ExecuteAsync(null);

            Assert.StartsWith("Error: Network timeout", vm.Status);
            Assert.False(vm.IsBusy);
        }

        private static (DecodeViewModel ViewModel, RecordingModbusService Service) CreateViewModel(bool connected = true)
        {
            var service = new RecordingModbusService { IsConnected = connected };
            var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 7)
            {
                IsConnected = connected
            };
            var connectionManager = new FakeConnectionManager(profile, service);
            return (new DecodeViewModel(connectionManager), service);
        }

        private sealed class FakeConnectionManager : IConnectionManager
        {
            private ConnectionProfile? _activeProfile;

            public FakeConnectionManager(ConnectionProfile profile, IModbusService service)
            {
                Profiles.Add(profile);
                _activeProfile = profile;
                ActiveService = service;
            }

            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile => _activeProfile;
            public IModbusService? ActiveService { get; private set; }

            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
            public event EventHandler<ConnectionProfile>? ProfileConnected;
            public event EventHandler<ConnectionProfile>? ProfileDisconnected;

            public void AddProfile(ConnectionProfile profile)
            {
                Profiles.Add(profile);
            }

            public void RemoveProfile(ConnectionProfile profile)
            {
                Profiles.Remove(profile);
            }

            public void SetActiveProfile(ConnectionProfile profile)
            {
                _activeProfile = profile;
                ActiveProfileChanged?.Invoke(this, profile);
            }

            public Task<bool> ConnectProfileAsync(ConnectionProfile profile)
            {
                profile.IsConnected = true;
                ProfileConnected?.Invoke(this, profile);
                return Task.FromResult(true);
            }

            public Task DisconnectProfileAsync(ConnectionProfile profile)
            {
                profile.IsConnected = false;
                ProfileDisconnected?.Invoke(this, profile);
                return Task.CompletedTask;
            }

            public Task DisconnectAllAsync() => Task.CompletedTask;

            public IModbusService? GetServiceForProfile(ConnectionProfile profile) => ActiveService;

            public void SaveProfiles()
            {
            }

            public void LoadProfiles()
            {
            }
        }

        private sealed class RecordingModbusService : IModbusService
        {
            public bool IsConnected { get; set; }
            public string BoundEndpoint => "test";
            public ModbusFrameLogger FrameLogger { get; } = new();
            public event EventHandler? ConnectionLost { add { } remove { } }
            public string? LastOperation { get; private set; }
            public byte LastUnitId { get; private set; }
            public int LastAddress { get; private set; }
            public int LastCount { get; private set; }
            public Exception? Error { get; set; }
            public ushort[]? HoldingRegistersResult { get; set; } = new ushort[] { 0x1234, 0x5678 };
            public ushort[]? InputRegistersResult { get; set; } = new ushort[] { 0x1234, 0x5678 };
            public bool[]? CoilsResult { get; set; } = new[] { true, false };
            public bool[]? DiscreteInputsResult { get; set; } = new[] { false, true };

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
            {
                IsConnected = true;
                return Task.FromResult(true);
            }

            public Task DisconnectAsync()
            {
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
                => Task.FromResult(new ConnectionDiagnosticResult());

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
            {
                Record("holding", unitId, startAddress, count);
                ThrowIfNeeded();
                return Task.FromResult(HoldingRegistersResult);
            }

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
            {
                Record("input", unitId, startAddress, count);
                ThrowIfNeeded();
                return Task.FromResult(InputRegistersResult);
            }

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
            {
                Record("coil", unitId, startAddress, count);
                ThrowIfNeeded();
                return Task.FromResult(CoilsResult);
            }

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
            {
                Record("discrete", unitId, startAddress, count);
                ThrowIfNeeded();
                return Task.FromResult(DiscreteInputsResult);
            }

            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value) => Task.CompletedTask;

            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values) => Task.CompletedTask;

            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value) => Task.CompletedTask;

            public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values) => Task.CompletedTask;

            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
                => Task.FromResult<ushort?>(null);

            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues)
                => Task.FromResult<ushort[]?>(null);

            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic)
                => Task.FromResult<DeviceIdentification?>(null);

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            private void Record(string operation, byte unitId, int address, int count)
            {
                LastOperation = operation;
                LastUnitId = unitId;
                LastAddress = address;
                LastCount = count;
            }

            private void ThrowIfNeeded()
            {
                if (Error != null)
                {
                    throw Error;
                }
            }
        }
    }
}

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
    public sealed class WatchViewModelMonitorTests
    {
        [Fact]
        public async Task Start_ReadsWatchedTagUntilItsValueArrives()
        {
            var (vm, tagService, service) = CreateViewModel(new ushort[] { 10 });

            vm.StartCommand.Execute(null);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while ((tagService.Tags[0].CurrentValue is not ushort value || value != 10)
                    && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }

                Assert.Equal((ushort)10, tagService.Tags[0].CurrentValue);
                Assert.True(tagService.Tags[0].LastUpdated > DateTime.MinValue);

                var entry = Assert.Single(tagService.WatchEntries);
                Assert.Equal((ushort)10, entry.CurrentValue);
                Assert.False(entry.IsStale);
                Assert.True(entry.LastUpdated > DateTime.MinValue);
            }
            finally
            {
                vm.StopCommand.Execute(null);
                vm.Dispose();
            }
        }

        [Fact]
        public async Task Start_DecodesMultiWordFloatTag()
        {
            // 12.5f == 0x41480000 -> registers 0x4148, 0x0000 at address 100.
            var (vm, tagService, service) = CreateViewModel(
                new ushort[] { 0x4148, 0x0000 },
                dataType: TagDataType.Float,
                address: 100);

            vm.StartCommand.Execute(null);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while ((tagService.Tags[0].CurrentValue is not float value || value != 12.5f)
                    && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }

                Assert.Equal(12.5f, Assert.IsType<float>(tagService.Tags[0].CurrentValue));
            }
            finally
            {
                vm.StopCommand.Execute(null);
                vm.Dispose();
            }
        }

        [Fact]
        public async Task Stop_HaltsReads()
        {
            var (vm, tagService, service) = CreateViewModel(new ushort[] { 1 });

            vm.StartCommand.Execute(null);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (tagService.Tags[0].CurrentValue is not ushort && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }

                Assert.IsType<ushort>(tagService.Tags[0].CurrentValue);

                // Stop, change the device value, and verify the watch stops following it.
                vm.StopCommand.Execute(null);
                Assert.False(vm.IsRunning);

                service.HoldingResult = new ushort[] { 99 };
                await Task.Delay(500);

                Assert.Equal((ushort)1, Assert.IsType<ushort>(tagService.Tags[0].CurrentValue));
            }
            finally
            {
                vm.StopCommand.Execute(null);
                vm.Dispose();
            }
        }

        private static (WatchViewModel ViewModel, TagService TagService, FakeTagService Service) CreateViewModel(
            ushort[] holdingResult,
            TagDataType dataType = TagDataType.UInt16,
            int address = 1)
        {
            var tagService = new TagService();
            var tag = new Tag
            {
                Name = "TestTag",
                Area = PlcArea.HoldingRegister,
                Address = address,
                DataType = dataType,
            };
            tagService.Tags.Add(tag);

            var service = new FakeTagService { HoldingResult = holdingResult };
            var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1) { IsConnected = true };
            var connectionManager = new FakeConnectionManager(profile, service);

            var vm = new WatchViewModel(
                tagService,
                new ThrowingMessageBoxService(),
                connectionManager: connectionManager,
                dispatcher: new SyncDispatcher());

            vm.AddTag(tag.Id);
            return (vm, tagService, service);
        }

        /// <summary>Never shows a dialog: the monitor must not be able to raise one.</summary>
        private sealed class ThrowingMessageBoxService : IMessageBoxService
        {
            public Task<DialogResult> ShowAsync(string message, string title = "", DialogButton buttons = DialogButton.Ok, DialogIcon icon = DialogIcon.None)
                => Task.FromResult(DialogResult.None);
        }

        private sealed class FakeConnectionManager : IConnectionManager
        {
            private readonly ConnectionProfile _profile;

            public FakeConnectionManager(ConnectionProfile profile, IModbusService service)
            {
                Profiles.Add(profile);
                _profile = profile;
                ActiveService = service;
            }

            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile => _profile;
            public IModbusService? ActiveService { get; private set; }
            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
            public event EventHandler<ConnectionProfile>? ProfileConnected;
            public event EventHandler<ConnectionProfile>? ProfileDisconnected;

            public void AddProfile(ConnectionProfile profile) => Profiles.Add(profile);
            public void RemoveProfile(ConnectionProfile profile) => Profiles.Remove(profile);
            public void SetActiveProfile(ConnectionProfile profile) => ActiveProfileChanged?.Invoke(this, profile);
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
            public void SaveProfiles() { }
            public void LoadProfiles() { }
        }

        private sealed class FakeTagService : IModbusService
        {
            public bool IsConnected => true;
            public string BoundEndpoint => "fake";
            public ModbusFrameLogger FrameLogger { get; } = new();
            public event EventHandler? ConnectionLost { add { } remove { } }

            public ushort[] HoldingResult { get; set; } = Array.Empty<ushort>();
            public int LastCount { get; private set; }

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId)
                => Task.FromResult(new ConnectionDiagnosticResult());

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count)
            {
                LastCount = count;
                return Task.FromResult<ushort[]?>(HoldingResult.Length >= count
                    ? HoldingResult.Take(count).ToArray()
                    : HoldingResult);
            }

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<ushort[]?>(Array.Empty<ushort>());

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(new[] { false });

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count)
                => Task.FromResult<bool[]?>(new[] { false });

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

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

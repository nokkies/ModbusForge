using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class MainViewModelParityTests
    {
        [Fact]
        public async Task Custom_entries_increment_by_one_for_single_register_types_and_by_two_for_multi_register_types()
        {
            var manager = new FakeConnectionManager();
            var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher());

            await vm.AddCustomEntryCommand.ExecuteAsync(null);
            Assert.Equal(1, vm.CustomEntries[0].Address);
            Assert.Equal("int", vm.CustomEntries[0].Type);

            // int is a single-register type, so it advances by 1.
            await vm.AddCustomEntryCommand.ExecuteAsync(null);
            Assert.Equal(2, vm.CustomEntries[1].Address);

            // real is a multi-register (32-bit) type, so it advances by 2.
            vm.CustomEntries[1].Type = "real";
            await vm.AddCustomEntryCommand.ExecuteAsync(null);
            Assert.Equal(4, vm.CustomEntries[2].Address);

            // int is single-register, so it advances by 1.
            vm.CustomEntries[2].Type = "int";
            await vm.AddCustomEntryCommand.ExecuteAsync(null);
            Assert.Equal(5, vm.CustomEntries[3].Address);

            vm.Dispose();
        }

        [Fact]
        public void Unit_configuration_store_keeps_custom_state_isolated_by_selected_unit()
        {
            var profile = new ConnectionProfile("Server", "127.0.0.1", 502, 1)
            {
                Mode = "Server",
                ServerUnitIds = "1,2"
            };
            var manager = new FakeConnectionManager(profile: profile);
            var store = new UnitConfigurationStore(new SyncDispatcher());
            store.SetConfiguration(1, new UnitIdConfiguration(1)
            {
                CustomEntries =
                {
                    new CustomEntry { Name = "Unit 1 tag", Address = 1 }
                }
            });
            store.SetConfiguration(2, new UnitIdConfiguration(2)
            {
                CustomEntries =
                {
                    new CustomEntry { Name = "Unit 2 tag", Address = 2 }
                }
            });
            store.PopulateAvailableUnitIds(new byte[] { 1, 2 });

            using var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                unitConfigurationStore: store);

            vm.SelectedUnitId = 1;
            Assert.Equal("Unit 1 tag", vm.CustomEntries[0].Name);

            vm.SelectedUnitId = 2;
            Assert.Equal("Unit 2 tag", vm.CustomEntries[0].Name);
            Assert.Equal((byte)2, vm.EffectiveUnitId);
        }

        [Fact]
        public async Task Continuous_custom_write_repeats_until_disabled()
        {
            var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1)
            {
                IsConnected = true,
                Status = "Connected"
            };
            var service = new ThrowingModbusService();
            var manager = new FakeConnectionManager(service, profile);
            var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher());

            vm.HoldingMonitorEnabled = false;
            manager.RaiseConnected(profile);
            await vm.AddCustomEntryCommand.ExecuteAsync(null);
            var entry = vm.CustomEntries[0];
            entry.WriteValue = "7";
            entry.PeriodMs = 50;
            entry.Continuous = true;
            vm.IsCustomWatchMonitoring = true;

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (service.WriteCount < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(service.WriteCount >= 2);
            Assert.True(entry.LastWriteUtc > DateTime.MinValue);

            vm.Dispose();
        }

        [Fact]
        public async Task Failed_monitor_read_is_tracked_paused_and_reported_without_disconnect()
        {
            var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1)
            {
                IsConnected = true,
                Status = "Connected"
            };
            var manager = new FakeConnectionManager(new ThrowingModbusService(), profile);
            var messageBox = new RecordingMessageBoxService();
            var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                messageBoxService: messageBox);

            vm.HoldingMonitorPeriodMs = 50;
            manager.RaiseConnected(profile);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (vm.HoldingMonitorEnabled && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.False(vm.HoldingMonitorEnabled);
            Assert.Equal(1, vm.HoldingMonitorFailureCount);
            Assert.True(vm.IsConnected);
            Assert.Equal(1, messageBox.CallCount);
            Assert.Contains("paused", messageBox.LastMessage, StringComparison.OrdinalIgnoreCase);

            vm.Dispose();
        }

        [Fact]
        public async Task Import_unit_ids_accepts_legacy_byte_list_format()
        {
            var profile = new ConnectionProfile("Server", "127.0.0.1", 502, 1)
            {
                Mode = "Server"
            };
            var manager = new FakeConnectionManager(profile: profile);
            var files = new FakeFileSystem();
            files.Files["unit-ids.json"] = "[0, 1, 2, 248, 2]";
            var dialogs = new FakeFileDialogService { OpenPath = "unit-ids.json" };
            using var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                fileDialogService: dialogs,
                fileSystem: files,
                unitConfigurationStore: new UnitConfigurationStore(new SyncDispatcher()));

            await vm.ImportUnitIdsCommand.ExecuteAsync(null);

            Assert.Equal(new byte[] { 1, 2 }, vm.AvailableUnitIds);
            Assert.Contains((byte)1, vm.UnitConfigurations.Keys);
            Assert.Contains((byte)2, vm.UnitConfigurations.Keys);
        }

        [Fact]
        public async Task Single_unit_commands_export_configuration_and_import_it_under_new_id()
        {
            var profile = new ConnectionProfile("Server", "127.0.0.1", 502, 1)
            {
                Mode = "Server"
            };
            var manager = new FakeConnectionManager(profile: profile);
            var store = new UnitConfigurationStore(new SyncDispatcher());
            store.SetConfiguration(1, new UnitIdConfiguration(1)
            {
                CustomEntries =
                {
                    new CustomEntry { Name = "Exported tag", Address = 4 }
                }
            });
            store.PopulateAvailableUnitIds(new byte[] { 1 });
            var files = new FakeFileSystem();
            var dialogs = new FakeFileDialogService { SavePath = "unit.mui", OpenPath = "unit.mui" };
            var input = new FakeInputDialogService { Input = "7", Accepted = true };
            using var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                fileDialogService: dialogs,
                inputDialogService: input,
                fileSystem: files,
                unitConfigurationStore: store);

            await vm.ExportUnitIdCommand.ExecuteAsync(null);
            Assert.Contains("unitConfigurations", files.Files["unit.mui"], StringComparison.OrdinalIgnoreCase);

            await vm.ImportUnitIdAsCommand.ExecuteAsync(null);

            Assert.Contains((byte)7, vm.UnitConfigurations.Keys);
            vm.SelectedUnitId = 7;
            Assert.Equal("Exported tag", vm.CustomEntries[0].Name);
        }

        [Fact]
        public async Task Project_commands_round_trip_profiles_unit_configurations_and_visible_tabs()
        {
            var first = new ConnectionProfile("First", "10.0.0.1", 502, 1)
            {
                Mode = "Server",
                ServerUnitIds = "1,2"
            };
            var second = new ConnectionProfile("Second", "10.0.0.2", 503, 2)
            {
                Mode = "Server",
                ServerUnitIds = "2"
            };
            var manager = new FakeConnectionManager(profile: first);
            manager.AddProfile(second);
            manager.SetActiveProfile(second);

            var store = new UnitConfigurationStore(new SyncDispatcher());
            store.SetConfiguration(1, new UnitIdConfiguration(1)
            {
                CustomEntries =
                {
                    new CustomEntry { Name = "Saved one", Address = 10 }
                }
            });
            store.SetConfiguration(2, new UnitIdConfiguration(2)
            {
                CustomEntries =
                {
                    new CustomEntry { Name = "Saved two", Address = 20 }
                }
            });
            store.PopulateAvailableUnitIds(new byte[] { 1, 2 });
            store.SelectedUnitId = 2;

            var files = new FakeFileSystem();
            var dialogs = new FakeFileDialogService { SavePath = "project.mfp", OpenPath = "project.mfp" };
            using var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                fileDialogService: dialogs,
                fileSystem: files,
                unitConfigurationStore: store);
            vm.IsRegistersTabVisible = false;

            await vm.SaveProjectCommand.ExecuteAsync(null);
            Assert.True(files.Files.ContainsKey("project.mfp"));
            Assert.Contains("unitConfigurations", files.Files["project.mfp"], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("profiles", files.Files["project.mfp"], StringComparison.OrdinalIgnoreCase);

            store.Clear();
            store.PopulateAvailableUnitIds(Array.Empty<byte>());
            manager.SetActiveProfile(first);
            vm.IsRegistersTabVisible = true;

            await vm.LoadProjectCommand.ExecuteAsync(null);

            Assert.Equal(2, manager.Profiles.Count);
            Assert.Equal("Second", manager.ActiveProfile?.Name);
            Assert.Contains((byte)1, vm.UnitConfigurations.Keys);
            Assert.Contains((byte)2, vm.UnitConfigurations.Keys);
            Assert.Equal((byte)2, vm.SelectedUnitId);
            Assert.Equal("Saved two", vm.CustomEntries[0].Name);
            Assert.False(vm.IsRegistersTabVisible);
        }

        private sealed class FakeFileDialogService : IFileDialogService
        {
            public string? SavePath { get; set; }
            public string? OpenPath { get; set; }

            public string? ShowSaveFileDialog(string title, string filter, string defaultFileName) => SavePath;
            public string? ShowOpenFileDialog(string title, string filter) => OpenPath;
            public Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName) => Task.FromResult(SavePath);
            public Task<string?> ShowOpenFileDialogAsync(string title, string filter) => Task.FromResult(OpenPath);
        }

        private sealed class FakeInputDialogService : IInputDialogService
        {
            public bool Accepted { get; set; }
            public string Input { get; set; } = string.Empty;

            public bool TryGetInput(string title, string prompt, string defaultValue, out string input)
            {
                input = Input;
                return Accepted;
            }
        }

        private sealed class FakeFileSystem : IFileSystem
        {
            public Dictionary<string, string> Files { get; } = new();

            public Task<string> ReadAllTextAsync(string path) =>
                Task.FromResult(Files.TryGetValue(path, out var text) ? text : string.Empty);

            public Task WriteAllTextAsync(string path, string contents)
            {
                Files[path] = contents;
                return Task.CompletedTask;
            }

            public bool FileExists(string path) => Files.ContainsKey(path);
        }

        private sealed class RecordingMessageBoxService : IMessageBoxService
        {
            public int CallCount { get; private set; }
            public string LastMessage { get; private set; } = string.Empty;

            public Task<DialogResult> ShowAsync(string message, string title, DialogButton button, DialogIcon icon)
            {
                CallCount++;
                LastMessage = message;
                return Task.FromResult(DialogResult.Ok);
            }
        }

        private sealed class FakeConnectionManager : IConnectionManager
        {
            public FakeConnectionManager(IModbusService? service = null, ConnectionProfile? profile = null)
            {
                ActiveService = service;
                ActiveProfile = profile;
                if (profile != null)
                {
                    Profiles.Add(profile);
                }
            }

            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile { get; private set; }
            public IModbusService? ActiveService { get; }

            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
            public event EventHandler<ConnectionProfile>? ProfileConnected;
            public event EventHandler<ConnectionProfile>? ProfileDisconnected;

            public void RaiseConnected(ConnectionProfile profile)
            {
                ProfileConnected?.Invoke(this, profile);
            }

            public void AddProfile(ConnectionProfile profile)
            {
                Profiles.Add(profile);
                ActiveProfile ??= profile;
            }

            public void RemoveProfile(ConnectionProfile profile)
            {
                Profiles.Remove(profile);
                if (ReferenceEquals(ActiveProfile, profile))
                {
                    ActiveProfile = Profiles.Count == 0 ? null : Profiles[0];
                    ActiveProfileChanged?.Invoke(this, ActiveProfile);
                }
            }

            public void SetActiveProfile(ConnectionProfile profile)
            {
                ActiveProfile = profile;
                ActiveProfileChanged?.Invoke(this, profile);
            }

            public Task<bool> ConnectProfileAsync(ConnectionProfile profile) => Task.FromResult(true);

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

        [Fact]
        public async Task ReadHoldingRegistersCommand_populates_grid_from_local_server()
        {
            var port = GetFreePort();
            var profile = new ConnectionProfile("Test Server", "127.0.0.1", port, 1)
            {
                Mode = "Server",
                ServerUnitIds = "1"
            };

            var connectionManager = new ConnectionManager(
                NullLogger<ConnectionManager>.Instance,
                NullLoggerFactory.Instance);
            connectionManager.Profiles.Add(profile);
            connectionManager.SetActiveProfile(profile);

            using var vm = new MainViewModel(
                connectionManager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher());

            // Start the server
            await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Assert.True(vm.IsConnected);
            Assert.True(vm.ReadHoldingRegistersCommand.CanExecute(null));
            Assert.True(vm.ReadInputRegistersCommand.CanExecute(null));
            Assert.True(vm.ReadCoilsCommand.CanExecute(null));
            Assert.True(vm.ReadDiscreteInputsCommand.CanExecute(null));

            // Read 20 holding registers starting at 0
            vm.HoldingRegisterStart = 0;
            vm.HoldingRegisterCount = 20;
            await vm.ReadHoldingRegistersCommand.ExecuteAsync(null);

            Assert.Equal(20, vm.HoldingRegisters.Count);
            Assert.Equal(0, vm.HoldingRegisters[0].Address);
            Assert.Equal("0", vm.HoldingRegisters[0].ValueText);
            Assert.Equal(1, vm.HoldingRegisters[1].Address);
            Assert.Equal("10", vm.HoldingRegisters[1].ValueText);

            await vm.ToggleConnectionCommand.ExecuteAsync(null);
        }

        [Fact]
        public void IsBusy_changes_raise_CanExecuteChanged_for_read_commands()
        {
            var manager = new FakeConnectionManager();
            var vm = new MainViewModel(
                manager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher());

            var events = 0;
            vm.ReadHoldingRegistersCommand.CanExecuteChanged += (s, e) => events++;

            vm.IsBusy = true;
            Assert.True(events > 0, "CanExecuteChanged should be raised when IsBusy becomes true.");

            var previous = events;
            vm.IsBusy = false;
            Assert.True(events > previous, "CanExecuteChanged should be raised when IsBusy becomes false.");

            vm.Dispose();
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class ThrowingModbusService : IModbusService
        {
            public int WriteCount { get; private set; }
            public bool IsConnected => true;
            public string BoundEndpoint => "test";
            public ModbusFrameLogger FrameLogger { get; } = new();
            public event EventHandler? ConnectionLost { add { } remove { } }

            public Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task DisconnectAsync() => Task.CompletedTask;
            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId) =>
                Task.FromResult(new ConnectionDiagnosticResult());

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count) =>
                Task.FromException<ushort[]?>(new InvalidOperationException("simulated holding-register failure"));

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count) => Task.FromResult<ushort[]?>(Array.Empty<ushort>());
            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value)
            {
                WriteCount++;
                return Task.CompletedTask;
            }
            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values)
            {
                WriteCount++;
                return Task.CompletedTask;
            }
            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count) => Task.FromResult<bool[]?>(Array.Empty<bool>());
            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count) => Task.FromResult<bool[]?>(Array.Empty<bool>());
            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value) => Task.CompletedTask;
            public Task WriteCoilsAsync(byte unitId, int startAddress, bool[] values) => Task.CompletedTask;
            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask) => Task.FromResult<ushort?>(null);
            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues) => Task.FromResult<ushort[]?>(null);
            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic) => Task.FromResult<DeviceIdentification?>(null);

            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

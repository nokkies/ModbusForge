using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class ConnectionManagerViewModelTests
    {
        private static (ConnectionManagerViewModel ViewModel, FakeConnectionManager ConnectionManager) CreateViewModel(params ConnectionProfile[] profiles)
        {
            var cm = new FakeConnectionManager(profiles);
            var dispatcher = new SyncDispatcher();
            var messageBoxService = new FakeMessageBoxService();
            var logger = NullLogger<ConnectionManagerViewModel>.Instance;

            var vm = new ConnectionManagerViewModel(cm, dispatcher, messageBoxService, logger);
            return (vm, cm);
        }

        [Fact]
        public void Constructor_WithProfiles_SelectsActiveOrFirstProfile()
        {
            var p1 = new ConnectionProfile("Profile 1", "127.0.0.1", 502, 1);
            var p2 = new ConnectionProfile("Profile 2", "127.0.0.1", 502, 2);

            var (vm, _) = CreateViewModel(p1, p2);

            Assert.NotNull(vm.SelectedProfile);
            Assert.Same(p1, vm.SelectedProfile);
            Assert.True(vm.HasSelection);
        }

        [Fact]
        public void AddCommand_Executes_AddsNewProfileAndSelectsIt()
        {
            var p1 = new ConnectionProfile("Profile 1", "127.0.0.1", 502, 1);
            var (vm, cm) = CreateViewModel(p1);

            vm.AddCommand.Execute(null);

            Assert.Equal(2, cm.Profiles.Count);
            Assert.Equal("Connection 2", vm.SelectedProfile?.Name);
        }

        [Fact]
        public void RemoveCommand_Executes_RemovesSelectedProfile()
        {
            var p1 = new ConnectionProfile("Profile 1", "127.0.0.1", 502, 1);
            var p2 = new ConnectionProfile("Profile 2", "127.0.0.1", 502, 2);
            var (vm, cm) = CreateViewModel(p1, p2);

            vm.SelectedProfile = p1;
            Assert.True(vm.RemoveCommand.CanExecute(null));

            vm.RemoveCommand.Execute(null);

            Assert.Single(cm.Profiles);
            Assert.Same(p2, vm.SelectedProfile);
        }

        [Fact]
        public void CloneCommand_Executes_ClonesSelectedProfile()
        {
            var p1 = new ConnectionProfile("Profile 1", "127.0.0.1", 502, 1);
            var (vm, cm) = CreateViewModel(p1);

            vm.CloneCommand.Execute(null);

            Assert.Equal(2, cm.Profiles.Count);
            Assert.Equal("Profile 1 (Copy)", vm.SelectedProfile?.Name);
        }

        [Fact]
        public void RefreshSerialPortsAsync_WhenProfileChangesRapidly_HandlesCancellationWithoutThrowing()
        {
            var p1 = new ConnectionProfile("Profile 1", "127.0.0.1", 502, 1);
            var p2 = new ConnectionProfile("Profile 2", "127.0.0.1", 502, 2);
            var (vm, _) = CreateViewModel(p1, p2);

            // Triggering rapid selection changes cancels any ongoing serial port refresh operation token.
            var exception = Record.Exception(() =>
            {
                vm.SelectedProfile = p2;
                vm.SelectedProfile = p1;
                vm.SelectedProfile = null;
            });

            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_CleansUpSubscriptionsAndTokens_DoesNotThrow()
        {
            var p1 = new ConnectionProfile("Profile 1", "127.0.0.1", 502, 1);
            var (vm, _) = CreateViewModel(p1);

            var exception = Record.Exception(() => vm.Dispose());

            Assert.Null(exception);
        }

        private sealed class FakeConnectionManager : IConnectionManager
        {
            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile { get; private set; }
            public IModbusService? ActiveService => null;

            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
            public event EventHandler<ConnectionProfile>? ProfileConnected;
            public event EventHandler<ConnectionProfile>? ProfileDisconnected;

            public FakeConnectionManager(params ConnectionProfile[] initialProfiles)
            {
                foreach (var p in initialProfiles)
                {
                    Profiles.Add(p);
                }
                if (Profiles.Count > 0)
                {
                    ActiveProfile = Profiles[0];
                }
            }

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
                ActiveProfile = profile;
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

            public IModbusService? GetServiceForProfile(ConnectionProfile profile) => null;

            public void SaveProfiles() { }

            public void LoadProfiles() { }
        }

        private sealed class FakeMessageBoxService : IMessageBoxService
        {
            public Task<DialogResult> ShowAsync(string message, string title, DialogButton button, DialogIcon icon)
                => Task.FromResult(DialogResult.Ok);
        }
    }
}

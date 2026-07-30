using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Avalonia.Tests.Fakes;
using ModbusForge.Avalonia.ViewModels;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class PreferencesViewModelTests
    {
        [Fact]
        public void Constructor_Loads_Settings_From_Service()
        {
            var settings = new FakeSettingsService
            {
                AutoReconnect = true,
                AutoReconnectIntervalMs = 2500,
                MaxConsoleMessages = 250,
                ApiPort = 8080,
                EnableApi = true
            };

            var vm = new PreferencesViewModel(settings);

            Assert.True(vm.AutoReconnect);
            Assert.Equal(2500, vm.AutoReconnectIntervalMs);
            Assert.Equal(250, vm.MaxConsoleMessages);
            Assert.Equal(8080, vm.ApiPort);
            Assert.True(vm.EnableApi);
        }

        [Fact]
        public void RegenerateApiKeyCommand_Updates_ApiKey()
        {
            var settings = new FakeSettingsService();
            var vm = new PreferencesViewModel(settings);

            var previous = vm.ApiKey;
            vm.RegenerateApiKeyCommand.Execute(null);

            Assert.NotEqual(previous, vm.ApiKey);
            Assert.False(string.IsNullOrWhiteSpace(vm.ApiKey));
        }

        [Fact]
        public async Task SaveCommand_Persists_Settings_To_Service()
        {
            var settings = new FakeSettingsService();
            var vm = new PreferencesViewModel(settings)
            {
                AutoReconnect = true,
                AutoReconnectIntervalMs = 3000,
                MaxConsoleMessages = 500,
                ApiPort = 9000,
                EnableApi = true,
                EnableApiAuthentication = true
            };

            await ((IAsyncRelayCommand)vm.SaveCommand).ExecuteAsync(null);

            Assert.True(settings.SaveWasCalled);
            Assert.True(settings.AutoReconnect);
            Assert.Equal(3000, settings.AutoReconnectIntervalMs);
            Assert.Equal(500, settings.MaxConsoleMessages);
            Assert.Equal(9000, settings.ApiPort);
            Assert.True(settings.EnableApi);
            Assert.True(settings.EnableApiAuthentication);
        }

        [Fact]
        public void CancelCommand_Raises_RequestClose_With_False()
        {
            var settings = new FakeSettingsService();
            var vm = new PreferencesViewModel(settings);

            bool? closeResult = null;
            vm.RequestClose += (_, result) => closeResult = result;

            vm.CancelCommand.Execute(null);

            Assert.False(closeResult);
        }
    }
}

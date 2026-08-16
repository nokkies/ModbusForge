using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.Tests.Fakes;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class MqttViewModelTests
    {
        [Fact]
        public async Task ApplyAndConnect_WhileRunning_RestartsTheGateway()
        {
            var settings = new FakeSettingsService { MqttSettings = new MqttSettings { Enabled = true, BrokerHost = "old-host" } };
            var gateway = new RecordingMqttGateway { Running = true, Connected = true };
            var vm = new MqttViewModel(settings, gateway, new SyncDispatcher());
            vm.Settings.BrokerHost = "new-host";

            await ((IAsyncRelayCommand)vm.ApplyAndConnectCommand).ExecuteAsync(null);

            Assert.Equal(1, gateway.DisconnectCalls);
            Assert.Equal(1, gateway.ConnectCalls);
            Assert.True(settings.SaveWasCalled);
            Assert.Equal("new-host", settings.MqttSettings.BrokerHost);
            Assert.Contains("new-host", vm.StatusMessage);
        }

        [Fact]
        public async Task ApplyAndConnect_WhenStopped_JustConnects()
        {
            var settings = new FakeSettingsService { MqttSettings = new MqttSettings { Enabled = true } };
            var gateway = new RecordingMqttGateway();
            var vm = new MqttViewModel(settings, gateway, new SyncDispatcher());

            await ((IAsyncRelayCommand)vm.ApplyAndConnectCommand).ExecuteAsync(null);

            Assert.Equal(0, gateway.DisconnectCalls);
            Assert.Equal(1, gateway.ConnectCalls);
            Assert.True(vm.IsConnected);
        }

        [Fact]
        public async Task Disconnect_StopsTheGateway()
        {
            var settings = new FakeSettingsService { MqttSettings = new MqttSettings { Enabled = true } };
            var gateway = new RecordingMqttGateway { Running = true, Connected = true };
            var vm = new MqttViewModel(settings, gateway, new SyncDispatcher());

            await ((IAsyncRelayCommand)vm.DisconnectCommand).ExecuteAsync(null);

            Assert.Equal(1, gateway.DisconnectCalls);
            Assert.False(vm.IsConnected);
            Assert.Equal("MQTT disconnected.", vm.StatusMessage);
        }

        [Fact]
        public void ConnectionStateChanged_UpdatesTheConnectedIndicator()
        {
            var settings = new FakeSettingsService { MqttSettings = new MqttSettings { Enabled = true } };
            var gateway = new RecordingMqttGateway { Running = true, Connected = true };
            var vm = new MqttViewModel(settings, gateway, new SyncDispatcher());
            Assert.True(vm.IsConnected);
            Assert.False(vm.IsRetrying);

            // The gateway reports a broker drop (raised from a worker thread in real use).
            gateway.Connected = false;
            gateway.RaiseConnectionStateChanged();

            Assert.False(vm.IsConnected);
            Assert.True(vm.IsRetrying, "a running gateway without a broker is 'retrying'");
        }

        /// <summary>Records gateway calls instead of touching a real broker.</summary>
        private sealed class RecordingMqttGateway : MqttGatewayService
        {
            public RecordingMqttGateway()
                : base(NullLogger<MqttGatewayService>.Instance)
            {
            }

            public int ConnectCalls { get; private set; }
            public int DisconnectCalls { get; private set; }
            public bool Running { get; set; }
            public bool Connected { get; set; }

            public override bool IsConnected => Connected;

            public override bool IsRunning => Running;

            public override Task ConnectAsync(CancellationToken cancellationToken = default)
            {
                ConnectCalls++;
                Running = true;
                Connected = true;
                return Task.CompletedTask;
            }

            public override Task DisconnectAsync()
            {
                DisconnectCalls++;
                Running = false;
                Connected = false;
                return Task.CompletedTask;
            }
        }
    }
}

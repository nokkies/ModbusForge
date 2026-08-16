using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class MqttGatewayServiceTests
    {
        [Fact]
        public void BuildTopic_ReplacesUnitTagAndAreaPlaceholders()
        {
            var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { TopicTemplate = "modbusforge/{UnitId}/{Tag}" });

            var topic = service.BuildTopic(new MqttTagUpdate
            {
                UnitId = 7,
                TagName = "Flow",
                Area = PlcArea.HoldingRegister,
                Address = 100,
            });

            Assert.Equal("modbusforge/7/Flow", topic);
        }

        [Fact]
        public void BuildTopic_IsCaseInsensitive()
        {
            var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { TopicTemplate = "{unitid}/{tag}/{area}/{address}" });

            var topic = service.BuildTopic(new MqttTagUpdate
            {
                UnitId = 2,
                TagName = "Run",
                Area = PlcArea.Coil,
                Address = 5,
            });

            Assert.Equal("2/Run/Coil/5", topic);
        }

        [Fact]
        public async Task Connect_WhenDisabled_DoesNotStartTheGateway()
        {
            using var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { Enabled = false, BrokerHost = "127.0.0.1", BrokerPort = 1 });

            await service.ConnectAsync();

            Assert.False(service.IsRunning);
            Assert.False(service.IsConnected);
        }

        [Fact]
        public async Task Connect_StartsReconnectLoop_EvenWhenTheBrokerIsUnreachable()
        {
            // Pointed at a closed port on loopback: the connect attempt fails fast,
            // but the gateway must keep running and retry (that is what a headless
            // deployment counts on while the broker is temporarily down).
            using var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { Enabled = true, BrokerHost = "127.0.0.1", BrokerPort = 1, PublishPeriodMs = 0 });

            await service.ConnectAsync();
            try
            {
                Assert.True(service.IsRunning, "the reconnect loop should be active after ConnectAsync");
                Assert.False(service.IsConnected);
            }
            finally
            {
                await service.DisconnectAsync();
            }

            Assert.False(service.IsRunning);
        }

        [Fact]
        public async Task Disconnect_RaisesConnectionStateChanged()
        {
            using var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            var raised = 0;
            service.ConnectionStateChanged += (_, _) => Interlocked.Increment(ref raised);

            await service.DisconnectAsync();
            Assert.True(raised >= 1, "disconnecting a stopped gateway still notifies listeners of the (dis)connected state");
        }
    }
}

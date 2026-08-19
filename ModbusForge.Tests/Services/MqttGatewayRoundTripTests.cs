using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Full round trips: the gateway publishes against a real (in-process, wire-level)
    /// MQTT broker and the test asserts what actually arrives on the wire - topics,
    /// payload JSON, QoS/retain flags, and the publish loop's cadence.
    /// </summary>
    public sealed class MqttGatewayRoundTripTests
    {
        [Fact]
        public async Task PublishSnapshot_DeliversEveryTagOnItsTemplatedTopic()
        {
            using var broker = new FakeMqttBroker();
            using var gateway = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance)
            {
                SnapshotProvider = () => new[]
                {
                    new MqttTagUpdate { UnitId = 1, TagName = "Flow", Area = PlcArea.HoldingRegister, Address = 10, Value = 42.5f },
                    new MqttTagUpdate { UnitId = 2, TagName = "Level", Area = PlcArea.Coil, Address = 3, Value = true },
                },
            };
            gateway.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                PublishPeriodMs = 0, // publish loop off; drive one explicit snapshot
            });

            await ConnectUntilConnectedAsync(gateway);

            await gateway.PublishSnapshotAsync();
            await WaitForAsync(() => broker.PublishedCount >= 2, TimeSpan.FromSeconds(15), () => $"published: {broker.PublishedCount}");

            var published = broker.GetAllPublished();
            var flow = published.Single(m => m.Topic == "modbusforge/1/Flow");
            var level = published.Single(m => m.Topic == "modbusforge/2/Level");

            // The payload is the contract for downstream consumers (dashboards,
            // scripts, historians) - it must be self-describing.
            var flowJson = Encoding.UTF8.GetString(flow.Payload);
            Assert.Contains("\"tagName\":\"Flow\"", flowJson);
            Assert.Contains("\"unitId\":1", flowJson);
            Assert.Contains("\"area\":\"HoldingRegister\"", flowJson);
            Assert.Contains("\"address\":10", flowJson);
            Assert.Contains("\"value\":42.5", flowJson);

            var levelJson = Encoding.UTF8.GetString(level.Payload);
            Assert.Contains("\"tagName\":\"Level\"", levelJson);
            Assert.Contains("\"area\":\"Coil\"", levelJson);
            Assert.Contains("\"value\":true", levelJson);
        }

        [Fact]
        public async Task PublishLoop_KeepsPublishingAtTheConfiguredPeriod()
        {
            using var broker = new FakeMqttBroker();
            using var gateway = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance)
            {
                SnapshotProvider = () => new[]
                {
                    new MqttTagUpdate { UnitId = 1, TagName = "Temp", Area = PlcArea.HoldingRegister, Address = 1, Value = 21f },
                },
            };
            gateway.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                PublishPeriodMs = 150,
            });

            await ConnectUntilConnectedAsync(gateway);

            // ConnectAsync started the publish loop; just wait for a few cycles.
            await WaitForAsync(
                () => broker.GetAllPublished().Count(m => m.Topic == "modbusforge/1/Temp") >= 3,
                TimeSpan.FromSeconds(20));
        }

        [Fact]
        public async Task PublishAsync_QualityOfServiceAndRetainReachTheWire()
        {
            using var broker = new FakeMqttBroker();
            using var gateway = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance)
            {
                SnapshotProvider = () => new[]
                {
                    new MqttTagUpdate { UnitId = 1, TagName = "Door", Area = PlcArea.Coil, Address = 7, Value = false },
                },
            };
            gateway.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                QualityOfService = 1,
                RetainMessages = true,
                PublishPeriodMs = 0,
            });

            await ConnectUntilConnectedAsync(gateway);

            await gateway.PublishSnapshotAsync();
            await WaitForAsync(() => broker.PublishedCount >= 1, TimeSpan.FromSeconds(15), () => $"published: {broker.PublishedCount}");

            var message = broker.GetAllPublished().Single();
            Assert.True(message.QualityOfService == 1, "the configured QoS must be on the wire");
            Assert.True(message.Retain, "the configured retain flag must be on the wire");
            // QoS 1 publishes only complete after the broker's PUBACK, so getting
            // here also proves the QoS 1 handshake was handled.
        }

        [Fact]
        public async Task PublishSnapshot_FreeFormTagName_PublishesOnASanitizedTopic()
        {
            // A tag name containing characters that are invalid in an MQTT
            // topic must still be published Ã¢â‚¬â€ on a sanitized topic.
            using var broker = new FakeMqttBroker();
            using var gateway = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance)
            {
                SnapshotProvider = () => new[]
                {
                    new MqttTagUpdate { UnitId = 1, TagName = "Temp #1 (A)", Area = PlcArea.HoldingRegister, Address = 20, Value = 1f },
                },
            };
            gateway.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                PublishPeriodMs = 0,
            });

            await ConnectUntilConnectedAsync(gateway);

            await gateway.PublishSnapshotAsync();
            await WaitForAsync(() => broker.PublishedCount >= 1, TimeSpan.FromSeconds(15), () => $"published: {broker.PublishedCount}");

            var message = broker.GetAllPublished().Single();
            Assert.Equal("modbusforge/1/Temp _1 (A)", message.Topic);
        }

        private static async Task ConnectUntilConnectedAsync(MqttGatewayService gateway)
        {
            await gateway.ConnectAsync();

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!gateway.IsConnected && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            Assert.True(gateway.IsConnected, "the gateway should connect to the in-process broker");
        }

        /// <param name="describe">Optional state description included in the timeout message.</param>
        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout, Func<string>? describe = null)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.Elapsed < timeout)
                await Task.Delay(25);

            var detail = describe is null ? string.Empty : $" (state: {describe()})";
            Assert.True(condition(), $"timed out waiting for the message to arrive{detail}");
        }
    }
}



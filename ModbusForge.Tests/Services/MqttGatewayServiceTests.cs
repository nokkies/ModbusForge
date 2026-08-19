using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        [Fact]
        public async Task UnreachableBroker_LogsOneCalmLineInsteadOfStackTraces()
        {
            // A down broker is an expected, self-healing condition: the log must
            // stay quiet (no warnings, no stack traces) with at most a single-line
            // heartbeat per attempt - this is what a headless deployment sees when
            // the broker is simply not up yet.
            var logger = new CapturingLogger();
            using var service = new MqttGatewayService(logger);
            service.ApplySettings(new MqttSettings { Enabled = true, BrokerHost = "127.0.0.1", BrokerPort = 1, PublishPeriodMs = 0 });

            await service.ConnectAsync();
            try
            {
                // Wait for at least one failed reconnect attempt's heartbeat.
                var stopwatch = Stopwatch.StartNew();
                while (logger.Entries.All(e => !e.Message.Contains("retrying", StringComparison.OrdinalIgnoreCase))
                       && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(50);
                }

                Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information
                    && e.Message.Contains("unreachable; retrying", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
            }
            finally
            {
                await service.DisconnectAsync();
            }
        }

        [Fact]
        public void BuildTopic_SanitizesCharactersInvalidInMqttTopics()
        {
            // '#' and '+' are reserved MQTT topic-filter characters and the
            // other invalids here (space, non-ASCII, control char) are not
            // allowed in a topic name at all — the tag name is free-form text.
            var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { TopicTemplate = "modbusforge/{UnitId}/{Tag}" });

            var topic = service.BuildTopic(new MqttTagUpdate
            {
                UnitId = 1,
                TagName = "Temp #1 (A)°\u0001",
                Area = PlcArea.HoldingRegister,
                Address = 100,
            });

            // '#' and the invalid characters become '_'; the space is legal
            // in an MQTT topic name and is kept.
            Assert.Equal("modbusforge/1/Temp _1 (A)__", topic);
        }

        [Fact]
        public async Task DisconnectDuringInitialConnect_CompletesCleanly_AndInFlightConnectFinishesWithoutError()
        {
            // Regression: DisconnectAsync while the FIRST connect is still in
            // flight used to dispose the token sources that ConnectAsync then
            // dereferenced to start the background loops (a null reference
            // once the in-flight connect completed), and could leave the
            // half-created client owned by neither side.
            using var broker = new FakeMqttBroker(connAckDelay: TimeSpan.FromSeconds(2));
            using var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                PublishPeriodMs = 0,
            });

            var connectTask = service.ConnectAsync(CancellationToken.None);
            await Task.Delay(300); // the connect is in flight (CONNACK is 2 s away)

            var stopwatch = Stopwatch.StartNew();
            await service.DisconnectAsync();
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"DisconnectAsync should not wait for the in-flight connect (took {stopwatch.Elapsed.TotalMilliseconds:F0} ms)");
            Assert.False(service.IsRunning);
            Assert.False(service.IsConnected);

            // The in-flight connect finishes ~2 s after it started; it must
            // complete WITHOUT an exception (pre-fix: NullReferenceException
            // when ConnectAsync tried to start the loops with the disposed
            // token sources).
            await connectTask;

            // The gateway must remain fully stopped, and usable again.
            Assert.False(service.IsRunning);
            await service.ConnectAsync();
            try
            {
                Assert.True(service.IsRunning);
            }
            finally
            {
                await service.DisconnectAsync();
            }
            Assert.False(service.IsRunning);
        }

        [Fact]
        public async Task ConnectAsync_WithAlreadyCancelledToken_FailsCleanly_WithoutZombieState()
        {
            // Regression: a cancelled first attempt used to leave the gateway
            // half-running — live token sources, no background loop, and
            // IsRunning stuck true with nothing to stop it.
            using var broker = new FakeMqttBroker();
            using var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                PublishPeriodMs = 0,
            });

            var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await service.ConnectAsync(cts.Token));

            // The failed start must not leave the gateway running or a client
            // behind, and the gateway must be startable afterwards.
            Assert.False(service.IsRunning);
            Assert.False(service.IsConnected);

            await service.ConnectAsync();
            try
            {
                Assert.True(service.IsRunning);
            }
            finally
            {
                await service.DisconnectAsync();
            }
            Assert.False(service.IsRunning);
        }

        [Fact]
        public async Task ConnectAsync_CancelledWhileInFlight_Completes_AndStaysConsistent()
        {
            // MQTTnet may or may not propagate the cancellation once the TCP
            // handshake is done; either way ConnectAsync must complete (not
            // hang) and leave the gateway in a consistent state that
            // DisconnectAsync can clean up.
            using var broker = new FakeMqttBroker(connAckDelay: TimeSpan.FromSeconds(30));
            using var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings
            {
                Enabled = true,
                BrokerHost = "127.0.0.1",
                BrokerPort = broker.Port,
                PublishPeriodMs = 0,
            });

            using var cts = new CancellationTokenSource();
            var connectTask = service.ConnectAsync(cts.Token);
            await Task.Delay(300); // connect in flight
            cts.Cancel();

            try
            {
                await connectTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when MQTTnet propagates the cancellation.
            }

            await service.DisconnectAsync();
            Assert.False(service.IsRunning);
            Assert.False(service.IsConnected);
        }

        private sealed class CapturingLogger : ILogger, ILogger<MqttGatewayService>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (Entries)
                {
                    Entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}

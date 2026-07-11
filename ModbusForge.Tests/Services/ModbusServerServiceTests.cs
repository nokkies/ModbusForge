using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusServerServiceTests : IDisposable
    {
        private readonly Mock<ILogger<ModbusServerService>> _serverLoggerMock;
        private readonly Mock<ILogger<ModbusTcpService>> _clientLoggerMock;
        private readonly ModbusServerService _serverService;
        private readonly int _testPort;

        public ModbusServerServiceTests()
        {
            _serverLoggerMock = new Mock<ILogger<ModbusServerService>>();
            _clientLoggerMock = new Mock<ILogger<ModbusTcpService>>();
            _serverService = new ModbusServerService(_serverLoggerMock.Object);
            _testPort = GetFreePort();
        }

        public void Dispose()
        {
            _serverService.Dispose();
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Fact]
        public async Task ConnectAsync_StartsServer_AndIsConnected()
        {
            var connected = await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            Assert.True(connected);
            Assert.True(_serverService.IsConnected);
        }

        [Fact]
        public async Task DisconnectAsync_ReturnsAndIsConnectedFalse()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            await _serverService.DisconnectAsync();

            Assert.False(_serverService.IsConnected);
        }

        [Fact]
        public async Task WriteSingleRegisterAsync_ReadHoldingRegistersAsync_RoundTrip()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            using var client = new ModbusTcpService(_clientLoggerMock.Object);
            var connected = await client.ConnectAsync("127.0.0.1", _testPort, "1");
            Assert.True(connected);

            const ushort expected = 12345;
            await client.WriteSingleRegisterAsync(1, 10, expected);
            var values = await client.ReadHoldingRegistersAsync(1, 10, 1);

            Assert.NotNull(values);
            Assert.Single(values);
            Assert.Equal(expected, values[0]);

            await client.DisconnectAsync();
        }

        [Fact]
        public async Task DisconnectAsync_FollowedByWait_CompletesWithoutHanging()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var disconnectTask = _serverService.DisconnectAsync();
            await disconnectTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(disconnectTask.IsCompleted);
            Assert.False(_serverService.IsConnected);
        }
    }
}

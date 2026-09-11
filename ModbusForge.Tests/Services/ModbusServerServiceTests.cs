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

        [Fact]
        public async Task WriteSingleRegisterAsync_NegativeAddress_Throws()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _serverService.WriteSingleRegisterAsync(1, -1, 42));
        }

        // The data store is 1-based (index 0 is an unused placeholder). Address 0 must be
        // rejected with a clear validation error instead of either writing into the
        // placeholder or blowing up deep inside the collection.
        [Fact]
        public async Task WriteSingleRegisterAsync_ZeroAddress_ThrowsWithClearMessage()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _serverService.WriteSingleRegisterAsync(1, 0, 42));
            Assert.Contains("Invalid holding register address 0", ex.Message);
        }

        [Fact]
        public async Task WriteSingleCoilAsync_ZeroAddress_ThrowsWithClearMessage()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _serverService.WriteSingleCoilAsync(1, 0, true));
            Assert.Contains("Invalid coil address 0", ex.Message);
        }

        [Fact]
        public async Task WriteCoilsAsync_RangeStartingAtZero_Throws()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _serverService.WriteCoilsAsync(1, 0, new[] { true, false }));
            Assert.Contains("Invalid coil range 0..1", ex.Message);
        }

        // Address 0 is readable (the store's index-0 placeholder returns its default
        // value) - the same convention the UI uses, where client-mode addresses 0 and 1
        // alias the first register. The parity test exercises this through MainViewModel.
        [Fact]
        public async Task ReadHoldingRegistersAsync_ZeroAddress_ReturnsPlaceholderDefaults()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var values = await _serverService.ReadHoldingRegistersAsync(1, 0, 2);

            Assert.NotNull(values);
            Assert.Equal(new ushort[] { 0, 10 }, values); // index 0 placeholder, index 1 = 1*10 seed
        }

        [Fact]
        public async Task ReadHoldingRegistersAsync_ValidOneBasedRange_ReturnsValues()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            await _serverService.WriteSingleRegisterAsync(1, 5, 77);

            var values = await _serverService.ReadHoldingRegistersAsync(1, 5, 2);

            Assert.NotNull(values);
            Assert.Equal(new ushort[] { 77, 60 }, values); // index 6 = 6*10 seed
        }

        [Fact]
        public async Task WriteSingleRegisterAsync_UnknownUnitId_ThrowsInsteadOfReturningPrimaryUnitStore()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _serverService.WriteSingleRegisterAsync(99, 5, 1));
            Assert.Contains("Data store not initialized for unit 99", ex.Message);
        }

        [Fact]
        public async Task ReadHoldingRegistersAsync_UnknownUnitId_ThrowsInsteadOfReturningPrimaryUnitStore()
        {
            await _serverService.ConnectAsync("127.0.0.1", _testPort, "1");

            var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                _serverService.ReadHoldingRegistersAsync(99, 5, 1));
            Assert.Contains("Data store not initialized for unit 99", ex.Message);
        }
    }
}

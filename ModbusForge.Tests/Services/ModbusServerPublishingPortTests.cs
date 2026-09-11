using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Tests for ModbusServerService. Data-path tests bind the server to 127.0.0.1 so a
    /// writable Modbus TCP server is never exposed on the machine's network interfaces
    /// during test runs (CI runners, developer LANs). Exactly one test exercises the
    /// 0.0.0.0 "publishing port" bind, which is the feature under test there.
    /// </summary>
    public class ModbusServerPublishingPortTests : IDisposable
    {
        private const string Loopback = "127.0.0.1";

        private readonly Mock<ILogger<ModbusServerService>> _loggerMock;
        private readonly ModbusServerService _serverService;
        private readonly int _testPort;
        private readonly string _testUnitIds = "1,2,3";

        public ModbusServerPublishingPortTests()
        {
            _loggerMock = new Mock<ILogger<ModbusServerService>>();
            _serverService = new ModbusServerService(_loggerMock.Object);

            // Find a free port for testing (bind first, then read the assigned port).
            _testPort = GetFreePort();
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
        public async Task PublishingPort_BindsToAllInterfaces_AndResolvesToActualAddresses()
        {
            // Arrange - the one test that deliberately binds to 0.0.0.0 (publishing port).
            string serverAddress = "0.0.0.0";

            // Act
            var connected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);

            // Assert
            Assert.True(connected, "Server should connect successfully when binding to 0.0.0.0");
            Assert.True(_serverService.IsConnected, "Server should report as connected");

            // The bound endpoint must resolve 0.0.0.0 to the actual interface addresses.
            var boundEndpoint = _serverService.BoundEndpoint;
            Assert.False(string.IsNullOrEmpty(boundEndpoint), "Bound endpoint should not be empty");
            Assert.False(boundEndpoint.Contains("0.0.0.0"), "Bound endpoint should resolve 0.0.0.0 to actual interface IPs");
            Assert.Contains(_testPort.ToString(), boundEndpoint);

            // Every resolved address must be a valid IPv4 address.
            var ipAddresses = boundEndpoint.Split(':').First().Split(',').Select(ip => ip.Trim());
            foreach (var ip in ipAddresses)
            {
                Assert.True(IPAddress.TryParse(ip, out _), $"'{ip}' should be a valid IP address");
                Assert.True(IPAddress.Parse(ip).AddressFamily == AddressFamily.InterNetwork,
                    $"'{ip}' should be an IPv4 address");
            }
        }

        [Fact]
        public async Task ClientCanConnect_WhenServerUsesPublishingPort()
        {
            // Arrange - data path: server on loopback only.
            var serverConnected = await _serverService.ConnectAsync(Loopback, _testPort, _testUnitIds);
            Assert.True(serverConnected, "Server should start successfully");

            // Act
            var clientService = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);
            var clientConnected = await clientService.ConnectAsync(Loopback, _testPort, "1");

            // Assert
            Assert.True(clientConnected, "Client should be able to connect to the server");
            Assert.True(clientService.IsConnected, "Client should report as connected");

            // Cleanup
            await clientService.DisconnectAsync();
            clientService.Dispose();
        }

        [Fact]
        public async Task MultipleClientsCanConnect_WhenServerUsesPublishingPort()
        {
            // Arrange
            const int clientCount = 3;

            var serverConnected = await _serverService.ConnectAsync(Loopback, _testPort, _testUnitIds);
            Assert.True(serverConnected, "Server should start successfully");

            var clients = new ModbusTcpService[clientCount];

            // Act - Connect multiple clients
            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);
                var connected = await clients[i].ConnectAsync(Loopback, _testPort, (i + 1).ToString());
                Assert.True(connected, $"Client {i + 1} should connect successfully");
            }

            // Assert - All clients should be connected
            for (int i = 0; i < clientCount; i++)
            {
                Assert.True(clients[i].IsConnected, $"Client {i + 1} should report as connected");
            }

            // Cleanup
            for (int i = 0; i < clientCount; i++)
            {
                await clients[i].DisconnectAsync();
                clients[i].Dispose();
            }
        }

        [Fact]
        public async Task ServerPublishesMultipleUnitIds_WhenUsingPublishingPort()
        {
            // Arrange
            string unitIds = "1,5,10-15,20"; // Test range notation

            // Act
            var connected = await _serverService.ConnectAsync(Loopback, _testPort, unitIds);

            // Assert
            Assert.True(connected, "Server should connect successfully");

            var availableUnitIds = _serverService.GetUnitIds().ToList();
            Assert.True(availableUnitIds.Count >= 8, "Should have at least 8 unit IDs (1,5,10,11,12,13,14,15,20)");

            Assert.Contains((byte)1, availableUnitIds);
            Assert.Contains((byte)5, availableUnitIds);
            Assert.Contains((byte)10, availableUnitIds);
            Assert.Contains((byte)11, availableUnitIds);
            Assert.Contains((byte)12, availableUnitIds);
            Assert.Contains((byte)13, availableUnitIds);
            Assert.Contains((byte)14, availableUnitIds);
            Assert.Contains((byte)15, availableUnitIds);
            Assert.Contains((byte)20, availableUnitIds);
        }

        [Fact]
        public async Task ServerWorksOnLocalhost_WhenNotUsingPublishingPort()
        {
            // Arrange - Use localhost instead of publishing port
            string serverAddress = Loopback;

            // Act
            var connected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);

            // Assert
            Assert.True(connected, "Server should connect successfully on localhost");
            Assert.True(_serverService.IsConnected, "Server should report as connected");

            // Client should be able to connect to localhost
            using var clientService = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);
            var clientConnected = await clientService.ConnectAsync(Loopback, _testPort, "1");
            Assert.True(clientConnected, "Client should be able to connect to server via localhost");

            var boundEndpoint = _serverService.BoundEndpoint;
            Assert.Contains(_testPort.ToString(), boundEndpoint);
        }

        [Fact]
        public async Task ClientFailsToConnect_WhenServerNotRunning()
        {
            // Arrange - Don't start server
            var clientService = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);

            // Act & Assert
            var connected = await clientService.ConnectAsync(Loopback, _testPort, "1");
            Assert.False(connected, "Client should fail to connect when server is not running");
            Assert.False(clientService.IsConnected, "Client should report as not connected");

            // Cleanup
            clientService.Dispose();
        }

        [Fact]
        public async Task ServerRejectsInvalidPort_WhenUsingPublishingPort()
        {
            // Arrange
            int invalidPort = -1;

            // Act & Assert
            var result = await _serverService.ConnectAsync(Loopback, invalidPort, _testUnitIds);
            Assert.False(result, "Server should fail to connect with invalid port");
        }

        public void Dispose()
        {
            // Clean up server
            if (_serverService.IsConnected)
            {
                _serverService.DisconnectAsync().Wait();
            }
            _serverService?.Dispose();
        }
    }
}

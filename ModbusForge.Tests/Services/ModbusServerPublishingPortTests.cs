using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;
using System.Linq;
using System.Threading;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Tests for ModbusServerService specifically testing the publishing port (0.0.0.0) functionality
    /// to ensure the server binds to all interfaces rather than just localhost.
    /// </summary>
    public class ModbusServerPublishingPortTests : IDisposable
    {
        private readonly Mock<ILogger<ModbusServerService>> _loggerMock;
        private readonly ModbusServerService _serverService;
        private readonly int _testPort;
        private readonly string _testUnitIds = "1,2,3";

        public ModbusServerPublishingPortTests()
        {
            _loggerMock = new Mock<ILogger<ModbusServerService>>();
            _serverService = new ModbusServerService(_loggerMock.Object);
            
            // Find a free port for testing
            _testPort = GetFreePort();
        }

        private int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Fact]
        public async Task ServerStarts_BindsToAllInterfaces_WhenUsingPublishingPort()
        {
            // Arrange
            string serverAddress = "0.0.0.0"; // Publishing port - bind to all interfaces

            // Act
            var connected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);

            // Assert
            Assert.True(connected, "Server should connect successfully when binding to 0.0.0.0");
            Assert.True(_serverService.IsConnected, "Server should report as connected");
            
            // Verify the bound endpoint shows actual interface addresses, not 0.0.0.0
            var boundEndpoint = _serverService.BoundEndpoint;
            Assert.False(string.IsNullOrEmpty(boundEndpoint), "Bound endpoint should not be empty");
            Assert.False(boundEndpoint.Contains("0.0.0.0"), "Bound endpoint should resolve 0.0.0.0 to actual interface IPs");
            
            // Should contain actual IP addresses and port
            Assert.Contains(_testPort.ToString(), boundEndpoint);
        }

        [Fact]
        public async Task ClientCanConnect_WhenServerUsesPublishingPort()
        {
            // Arrange
            string serverAddress = "0.0.0.0";
            
            // Start server on publishing port
            var serverConnected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);
            Assert.True(serverConnected, "Server should start successfully");

            // Act - Create client and connect to actual bound address
            var clientService = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);
            var boundEndpoint = _serverService.BoundEndpoint;
            var actualIp = boundEndpoint.Split(':')[0]; // Get first IP from bound endpoint
            
            var clientConnected = await clientService.ConnectAsync(actualIp, _testPort, "1");

            // Assert
            Assert.True(clientConnected, "Client should be able to connect to server bound to publishing port");
            Assert.True(clientService.IsConnected, "Client should report as connected");

            // Cleanup
            await clientService.DisconnectAsync();
            clientService.Dispose();
        }

        [Fact]
        public async Task MultipleClientsCanConnect_WhenServerUsesPublishingPort()
        {
            // Arrange
            string serverAddress = "0.0.0.0";
            const int clientCount = 3;
            
            // Start server on publishing port
            var serverConnected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);
            Assert.True(serverConnected, "Server should start successfully");

            var boundEndpoint = _serverService.BoundEndpoint;
            var actualIp = boundEndpoint.Split(':')[0]; // Get first IP from bound endpoint
            var clients = new ModbusTcpService[clientCount];

            // Act - Connect multiple clients
            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);
                var connected = await clients[i].ConnectAsync(actualIp, _testPort, (i + 1).ToString());
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
            string serverAddress = "0.0.0.0";
            string unitIds = "1,5,10-15,20"; // Test range notation

            // Act
            var connected = await _serverService.ConnectAsync(serverAddress, _testPort, unitIds);

            // Assert
            Assert.True(connected, "Server should connect successfully");
            
            var availableUnitIds = _serverService.GetUnitIds().ToList();
            Assert.True(availableUnitIds.Count >= 8, "Should have at least 8 unit IDs (1,5,10,11,12,13,14,15,20)");
            
            Assert.True(availableUnitIds.Contains(1));
            Assert.True(availableUnitIds.Contains(5));
            Assert.True(availableUnitIds.Contains(10));
            Assert.True(availableUnitIds.Contains(11));
            Assert.True(availableUnitIds.Contains(12));
            Assert.True(availableUnitIds.Contains(13));
            Assert.True(availableUnitIds.Contains(14));
            Assert.True(availableUnitIds.Contains(15));
            Assert.True(availableUnitIds.Contains(20));
        }

        [Fact]
        public async Task BoundEndpointShowsActualInterfaces_WhenUsingPublishingPort()
        {
            // Arrange
            string serverAddress = "0.0.0.0";

            // Act
            var connected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);
            Assert.True(connected, "Server should connect successfully");

            var boundEndpoint = _serverService.BoundEndpoint;

            // Assert
            Assert.False(string.IsNullOrEmpty(boundEndpoint), "Bound endpoint should not be empty");
            Assert.Contains(_testPort.ToString(), boundEndpoint);
            
            // Should resolve to actual interface addresses, not 0.0.0.0
            Assert.DoesNotContain("0.0.0.0", boundEndpoint);
            
            // Should contain valid IP addresses (IPv4 format)
            var ipPortPart = boundEndpoint.Split(':').First();
            var ipAddresses = ipPortPart.Split(',').Select(ip => ip.Trim());
            
            foreach (var ip in ipAddresses)
            {
                Assert.True(IPAddress.TryParse(ip, out _), $"'{ip}' should be a valid IP address");
                Assert.True(IPAddress.Parse(ip).AddressFamily == AddressFamily.InterNetwork, 
                    $"'{ip}' should be an IPv4 address");
            }
        }

        [Fact]
        public async Task ServerWorksOnLocalhost_WhenNotUsingPublishingPort()
        {
            // Arrange - Use localhost instead of publishing port
            string serverAddress = "127.0.0.1";

            // Act
            var connected = await _serverService.ConnectAsync(serverAddress, _testPort, _testUnitIds);

            // Assert
            Assert.True(connected, "Server should connect successfully on localhost");
            Assert.True(_serverService.IsConnected, "Server should report as connected");
            
            var boundEndpoint = _serverService.BoundEndpoint;
            Assert.Contains("127.0.0.1", boundEndpoint);
            Assert.Contains(_testPort.ToString(), boundEndpoint);
        }

        [Fact]
        public async Task ClientFailsToConnect_WhenServerNotRunning()
        {
            // Arrange - Don't start server
            var clientService = new ModbusTcpService(new Mock<ILogger<ModbusTcpService>>().Object);

            // Act & Assert
            var connected = await clientService.ConnectAsync("127.0.0.1", _testPort, "1");
            Assert.False(connected, "Client should fail to connect when server is not running");
            Assert.False(clientService.IsConnected, "Client should report as not connected");

            // Cleanup
            clientService.Dispose();
        }

        [Fact]
        public async Task ServerRejectsInvalidPort_WhenUsingPublishingPort()
        {
            // Arrange
            string serverAddress = "0.0.0.0";
            int invalidPort = -1;

            // Act & Assert
            var result = await _serverService.ConnectAsync(serverAddress, invalidPort, _testUnitIds);
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

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;
using System.Threading;

namespace ModbusForge.Tests.Services
{
    public class ModbusMultiUnitServerTests : IDisposable
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly ModbusMultiUnitServer _server;
        private readonly int _testPort;

        public ModbusMultiUnitServerTests()
        {
            _loggerMock = new Mock<ILogger>();
            _server = new ModbusMultiUnitServer(_loggerMock.Object);
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
        public async Task Server_RejectsLargeLengthInHeader()
        {
            _server.Start(new IPEndPoint(IPAddress.Loopback, _testPort), new byte[] { 1 });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _testPort);
            var stream = client.GetStream();

            byte[] header = new byte[7];
            header[0] = 0x00; header[1] = 0x01; // Transaction ID
            header[2] = 0x00; header[3] = 0x00; // Protocol ID
            header[4] = 0xFF; header[5] = 0xFF; // Length (65535) - too large
            header[6] = 0x01; // Unit ID

            await stream.WriteAsync(header, 0, header.Length);

            // Wait for the server's reaction (response or disconnect) without a fixed sleep:
            // poll the stream with short read timeouts until a deadline. A vulnerable server
            // blocks in ReadExactAsync(65534 bytes) and never closes the connection, so the
            // read keeps timing out and `read` stays -1; a fixed server closes the stream,
            // which ReadAsync reports as 0 bytes.
            byte[] buffer = new byte[10];
            int read = -1;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var readCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                    read = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                    break;
                }
                catch (OperationCanceledException)
                {
                    // No reaction yet; keep polling until the deadline.
                }
            }

            Assert.Equal(0, read);
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
        }
    }
}

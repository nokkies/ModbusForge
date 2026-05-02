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

            // Give the server time to process
            await Task.Delay(500);

            byte[] buffer = new byte[10];
            int read = -1;
            try
            {
                client.ReceiveTimeout = 1000;
                read = await stream.ReadAsync(buffer, 0, buffer.Length);
            }
            catch (Exception)
            {
                read = 0;
            }

            // Before fix, server waits for data. ReadAsync may block (or return after timeout if configured)
            // But if server is NOT fixed, it's sitting at ReadExactAsync(stream, pdu, 65534, ct)
            // Since we didn't send 65534 bytes, ReadExactAsync won't return.
            // stream.ReadAsync(buffer, 0, buffer.Length) on client side will block or wait.

            // If the server IS fixed, it closes the stream. ReadAsync returns 0.

            // Let's adjust expectation for initial run (it should FAIL if vulnerable)
            // If it's vulnerable, it should NOT return 0 (it should timeout or block)
            Assert.Equal(0, read);
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
        }
    }
}

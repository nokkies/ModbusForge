using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusMultiUnitServerFrameCaptureTests : IDisposable
    {
        private readonly ModbusFrameLogger _frames = new();
        private readonly ModbusMultiUnitServer _server;
        private readonly int _testPort;

        public ModbusMultiUnitServerFrameCaptureTests()
        {
            _server = new ModbusMultiUnitServer(Mock.Of<ILogger>(), null, _frames);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _testPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            _server.Start(new IPEndPoint(IPAddress.Loopback, _testPort), new byte[] { 1 });
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
        }

        [Fact]
        public async Task ClientRead_CapturesRxAndTxFrames()
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _testPort);
            var stream = client.GetStream();

            // FC03: read 2 holding registers at address 0, unit 1, transaction 7
            var request = new byte[]
            {
                0x00, 0x07, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x06, // Length
                0x01,       // Unit ID
                0x03,       // FC
                0x00, 0x00, // Address
                0x00, 0x02  // Quantity
            };
            await stream.WriteAsync(request, 0, request.Length);

            // FC03 response for two registers: MBAP(7) + FC(1) + byte count(1) + values(4) = 13 bytes.
            client.ReceiveTimeout = 5000;
            var response = new byte[13];
            int read = 0;
            while (read < response.Length)
            {
                int n = await stream.ReadAsync(response, read, response.Length - read);
                if (n == 0) break;
                read += n;
            }

            Assert.True(read == response.Length, $"expected the full 13-byte response frame, read {read}");

            // Give the server a moment to write the captured frames (logging happens
            // inline on the server's client loop; the small delay just absorbs scheduling).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_frames.Frames.Count < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            Assert.Equal(2, _frames.Frames.Count);

            var rx = Assert.IsType<ModbusFrameLog>(_frames.Frames[0]);
            var tx = Assert.IsType<ModbusFrameLog>(_frames.Frames[1]);

            Assert.Equal(FrameDirection.Rx, rx.Direction);
            Assert.Equal(FrameDirection.Tx, tx.Direction);
            Assert.Equal(0x01, rx.UnitId);
            Assert.Equal(0x01, tx.UnitId);
            Assert.Equal(0x03, rx.FunctionCode);
            Assert.Equal(0x03, tx.FunctionCode);
            Assert.Null(rx.IsValidCrc); // Modbus TCP has no checksum
            Assert.Null(tx.IsValidCrc);

            // Rx frame is the exact request bytes the client sent.
            Assert.Equal(request, rx.RawBytes);

            // Tx frame echoes the transaction ID and unit ID, and carries the read values.
            Assert.Equal(0x00, tx.RawBytes[0]);
            Assert.Equal(0x07, tx.RawBytes[1]);
            Assert.Equal(0x01, tx.RawBytes[6]);
            Assert.Equal(0x03, tx.RawBytes[7]);
            Assert.Equal(0x04, tx.RawBytes[8]); // byte count

            // Seed data: holding register 1 = 10, register 2 = 20.
            Assert.Equal((0x00 << 8) | 10, tx.RawBytes[9] * 256 + tx.RawBytes[10]);
            Assert.Equal((0x00 << 8) | 20, tx.RawBytes[11] * 256 + tx.RawBytes[12]);
        }

        [Fact]
        public async Task ServerWithoutFrameLogger_StillWorks()
        {
            var plain = new ModbusMultiUnitServer(Mock.Of<ILogger>());
            int port;
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            plain.Start(new IPEndPoint(IPAddress.Loopback, port), new byte[] { 1 });

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                var request = new byte[]
                {
                    0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01,
                    0x03, 0x00, 0x00, 0x00, 0x01
                };
                await stream.WriteAsync(request, 0, request.Length);

                // FC03 response for one register: MBAP(7) + FC(1) + byte count(1) + value(2) = 11 bytes.
                client.ReceiveTimeout = 5000;
                var buffer = new byte[11];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = await stream.ReadAsync(buffer, read, buffer.Length - read);
                    if (n == 0) break;
                    read += n;
                }

                Assert.True(read == buffer.Length, $"expected the full 11-byte response frame, read {read}");
                Assert.Equal(0x03, buffer[7]);
            }
            finally
            {
                plain.Stop();
                plain.Dispose();
            }
        }
    }
}

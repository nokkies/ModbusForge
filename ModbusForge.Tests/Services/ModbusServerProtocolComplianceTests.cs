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
    /// <summary>
    /// Regression tests for Modbus protocol-compliance fixes in <see cref="ModbusMultiUnitServer"/>:
    /// strict FC05 coil values, broadcast (Unit ID 0) semantics, and MBAP protocol ID validation.
    /// </summary>
    public class ModbusServerProtocolComplianceTests : IDisposable
    {
        private readonly ModbusMultiUnitServer _server;
        private readonly int _port;

        public ModbusServerProtocolComplianceTests()
        {
            _server = new ModbusMultiUnitServer(Mock.Of<ILogger>());
            _port = GetFreePort();
            _server.Start(new IPEndPoint(IPAddress.Loopback, _port), new byte[] { 1, 2 });
        }

        [Fact]
        public async Task WriteSingleCoil_AcceptsStrictOnAndOffValues()
        {
            var conn = await ConnectAsync(_port);
            using var client = conn.Client;
            var stream = conn.Stream;

            // 0xFF00 = ON
            var echo = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 5, 0x00, 0x00, 0xFF, 0x00 });
            Assert.Equal(new byte[] { 5, 0x00, 0x00, 0xFF, 0x00 }, echo);

            var coilOn = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 1, 0x00, 0x00, 0x00, 0x01 });
            Assert.Equal(1, coilOn[1]);
            Assert.Equal(1, coilOn[2] & 0x01);

            // 0x0000 = OFF
            await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 5, 0x00, 0x00, 0x00, 0x00 });
            var coilOff = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 1, 0x00, 0x00, 0x00, 0x01 });
            Assert.Equal(0, coilOff[2] & 0x01);
        }

        [Fact]
        public async Task WriteSingleCoil_RejectsNonStrictValue_WithIllegalDataValue()
        {
            var conn = await ConnectAsync(_port);
            using var client = conn.Client;
            var stream = conn.Stream;

            var response = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 5, 0x00, 0x00, 0x12, 0x34 });
            Assert.Equal(5 | 0x80, response[0]);
            Assert.Equal(3, response[1]); // Illegal data value

            // The coil must be unchanged (the previous code silently read any non-0xFF high byte as OFF).
            var coil = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 1, 0x00, 0x00, 0x00, 0x01 });
            Assert.Equal(0, coil[2] & 0x01);
        }

        [Fact]
        public async Task BroadcastWrite_IsAppliedToAllUnits_WithoutResponse()
        {
            var conn = await ConnectAsync(_port);
            using var client = conn.Client;
            var stream = conn.Stream;

            // Broadcast FC06 (Unit ID 0): holding register PDU address 4 = 0xABCD.
            // Per spec: no response is sent. The connection must stay usable, so the next
            // (unit-addressed) request on the SAME connection returns a cleanly framed answer.
            await WriteRequestAsync(stream, unitId: 0, new byte[] { 6, 0x00, 0x04, 0xAB, 0xCD });

            var unit1 = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 3, 0x00, 0x04, 0x00, 0x01 });
            Assert.Equal(3, unit1[0]);
            Assert.Equal((ushort)0xABCD, (ushort)((unit1[2] << 8) | unit1[3]));

            var unit2 = await SendAndReceiveAsync(stream, unitId: 2, new byte[] { 3, 0x00, 0x04, 0x00, 0x01 });
            Assert.Equal((ushort)0xABCD, (ushort)((unit2[2] << 8) | unit2[3]));
        }

        [Fact]
        public async Task BroadcastRead_ReceivedNoResponse()
        {
            var conn = await ConnectAsync(_port);
            using var client = conn.Client;
            var stream = conn.Stream;

            await WriteRequestAsync(stream, unitId: 0, new byte[] { 3, 0x00, 0x04, 0x00, 0x01 });

            // No response may arrive for the broadcast. Verify by sending a valid unit-addressed
            // read afterwards and confirming its response is the first bytes on the wire.
            var response = await SendAndReceiveAsync(stream, unitId: 1, new byte[] { 3, 0x00, 0x04, 0x00, 0x01 });
            Assert.Equal(3, response[0]);
            Assert.Equal(2, response[1]);
        }

        [Fact]
        public async Task InvalidMbapProtocolId_ConnectionIsClosed()
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            var stream = client.GetStream();

            // Well-formed length, but protocol ID 0x0001 instead of 0x0000.
            var header = new byte[] { 0x00, 0x01, 0x00, 0x01, 0x00, 0x06, 0x01 };
            await stream.WriteAsync(header);

            // The server must close the connection promptly. A sync read (bounded by the 2 s
            // watchdog) returns 0 on close; a non-compliant server would stay open waiting for
            // the PDU bytes and the read would still be pending when the watchdog fires.
            var readTask = Task.Run(() => stream.Read(new byte[8], 0, 8));
            var finished = await Task.WhenAny(readTask, Task.Delay(2000));
            Assert.True(
                ReferenceEquals(readTask, finished),
                "server should have closed the connection after a bad protocol ID");

            Assert.True(readTask.IsCompletedSuccessfully || readTask.IsFaulted);
            if (readTask.IsCompletedSuccessfully)
                Assert.Equal(0, readTask.Result);
        }

        private static async Task<(TcpClient Client, NetworkStream Stream)> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return (client, client.GetStream());
        }

        private static async Task<byte[]> SendAndReceiveAsync(NetworkStream stream, byte unitId, byte[] pdu)
        {
            await WriteRequestAsync(stream, unitId, pdu);
            return await ReadResponseAsync(stream);
        }

        private static async Task WriteRequestAsync(NetworkStream stream, byte unitId, byte[] pdu)
        {
            var request = new byte[7 + pdu.Length];
            request[0] = 0x00; request[1] = 0x01;               // Transaction ID
            request[2] = 0x00; request[3] = 0x00;               // Protocol ID
            request[4] = (byte)((pdu.Length + 1) >> 8);
            request[5] = (byte)((pdu.Length + 1) & 0xFF);       // Length
            request[6] = unitId;
            Buffer.BlockCopy(pdu, 0, request, 7, pdu.Length);
            await stream.WriteAsync(request);
        }

        private static async Task<byte[]> ReadResponseAsync(NetworkStream stream)
        {
            var header = new byte[7];
            await ReadExactlyAsync(stream, header, header.Length);
            int responseLength = ((header[4] << 8) | header[5]) - 1;
            var responsePdu = new byte[responseLength];
            await ReadExactlyAsync(stream, responsePdu, responseLength);
            return responsePdu;
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
                if (read == 0) throw new InvalidOperationException("Connection closed before the response was complete.");
                offset += read;
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

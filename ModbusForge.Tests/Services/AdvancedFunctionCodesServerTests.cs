using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Services.Messages;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Exercises the FC22/FC23/FC43 handlers of <see cref="ModbusMultiUnitServer"/> over a
    /// real loopback socket, so both the MBAP framing and the PDU handling are covered.
    /// </summary>
    public class AdvancedFunctionCodesServerTests : IDisposable
    {
        private const byte UnitId = 1;

        private readonly ModbusMultiUnitServer _server;
        private readonly int _port;

        public AdvancedFunctionCodesServerTests()
        {
            _server = new ModbusMultiUnitServer(Mock.Of<ILogger>());
            _port = GetFreePort();
            _server.Start(new IPEndPoint(IPAddress.Loopback, _port), new byte[] { UnitId });
        }

        [Fact]
        public async Task MaskWriteRegister_AppliesMasksAndEchoesRequest()
        {
            // DataStore seeds holding register 1 (PDU address 0) with 10.
            var pdu = new byte[] { 22, 0x00, 0x00, 0x00, 0xF0, 0x00, 0x05 };

            var response = await SendAsync(pdu);

            Assert.Equal(pdu, response);

            var readBack = await SendAsync(new byte[] { 3, 0x00, 0x00, 0x00, 0x01 });
            ushort value = (ushort)((readBack[2] << 8) | readBack[3]);
            Assert.Equal((ushort)((10 & 0x00F0) | (0x0005 & ~0x00F0)), value);
        }

        [Fact]
        public async Task MaskWriteRegister_ReturnsIllegalDataAddress_WhenOutOfRange()
        {
            var response = await SendAsync(new byte[] { 22, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00 });

            Assert.Equal(22 | 0x80, response[0]);
            Assert.Equal(2, response[1]);
        }

        [Fact]
        public async Task MaskWriteRegister_ReturnsIllegalDataValue_WhenPduTruncated()
        {
            var response = await SendAsync(new byte[] { 22, 0x00, 0x00, 0x00 });

            Assert.Equal(22 | 0x80, response[0]);
            Assert.Equal(3, response[1]);
        }

        [Fact]
        public async Task ReadWriteMultipleRegisters_WritesBeforeReadingSameRange()
        {
            // Write 0x1111,0x2222 to PDU addresses 0..1 and read back the same two registers.
            var pdu = new byte[]
            {
                23,
                0x00, 0x00, // read start
                0x00, 0x02, // read quantity
                0x00, 0x00, // write start
                0x00, 0x02, // write quantity
                0x04,       // write byte count
                0x11, 0x11, 0x22, 0x22
            };

            var response = await SendAsync(pdu);

            Assert.Equal(23, response[0]);
            Assert.Equal(4, response[1]);
            Assert.Equal(new byte[] { 0x11, 0x11, 0x22, 0x22 }, response.Skip(2).ToArray());
        }

        [Fact]
        public async Task ReadWriteMultipleRegisters_ReturnsIllegalDataValue_OnByteCountMismatch()
        {
            var pdu = new byte[]
            {
                23, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x02, 0x11, 0x11
            };

            var response = await SendAsync(pdu);

            Assert.Equal(23 | 0x80, response[0]);
            Assert.Equal(3, response[1]);
        }

        [Fact]
        public async Task ReadDeviceIdentification_ReturnsBasicObjects()
        {
            _server.DeviceIdentification = DeviceIdentification.CreateDefault("9.9.9");

            var response = await SendAsync(new byte[] { 0x2B, 0x0E, 0x01, 0x00 });
            var parsed = Parse(response);

            Assert.Equal(0x00, parsed.MoreFollows);
            Assert.Equal(3, parsed.Objects.Count);
            Assert.Equal("ModbusForge", parsed.Objects[DeviceIdObject.VendorName]);
            Assert.Equal("MF-TCP", parsed.Objects[DeviceIdObject.ProductCode]);
            Assert.Equal("9.9.9", parsed.Objects[DeviceIdObject.MajorMinorRevision]);
        }

        [Fact]
        public async Task ReadDeviceIdentification_ReturnsSingleObject_ForIndividualAccess()
        {
            var response = await SendAsync(new byte[] { 0x2B, 0x0E, 0x04, DeviceIdObject.ProductName });
            var parsed = Parse(response);

            Assert.Single(parsed.Objects);
            Assert.Equal(_server.DeviceIdentification.ProductName, parsed.Objects[DeviceIdObject.ProductName]);
        }

        [Fact]
        public async Task ReadDeviceIdentification_ReturnsIllegalDataAddress_ForUnknownObject()
        {
            var response = await SendAsync(new byte[] { 0x2B, 0x0E, 0x04, 0x7E });

            Assert.Equal(0x2B | 0x80, response[0]);
            Assert.Equal(2, response[1]);
        }

        [Fact]
        public async Task ReadDeviceIdentification_ReturnsIllegalFunction_ForUnsupportedMeiType()
        {
            var response = await SendAsync(new byte[] { 0x2B, 0x0D, 0x01, 0x00 });

            Assert.Equal(0x2B | 0x80, response[0]);
            Assert.Equal(1, response[1]);
        }

        private static ReadDeviceIdentificationResponse Parse(byte[] pdu)
        {
            var frame = new byte[pdu.Length + 1];
            frame[0] = UnitId;
            Buffer.BlockCopy(pdu, 0, frame, 1, pdu.Length);

            var response = new ReadDeviceIdentificationResponse();
            response.Initialize(frame);
            return response;
        }

        private async Task<byte[]> SendAsync(byte[] pdu)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            var stream = client.GetStream();

            var request = new byte[7 + pdu.Length];
            request[0] = 0x00; request[1] = 0x01;               // Transaction ID
            request[2] = 0x00; request[3] = 0x00;               // Protocol ID
            request[4] = (byte)((pdu.Length + 1) >> 8);
            request[5] = (byte)((pdu.Length + 1) & 0xFF);       // Length
            request[6] = UnitId;
            Buffer.BlockCopy(pdu, 0, request, 7, pdu.Length);

            await stream.WriteAsync(request);

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

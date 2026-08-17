using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using PacketDotNet;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class PcapImportServiceTests
    {
        [Fact]
        public void Import_ExtractsModbusTcpFrameFromPcap()
        {
            var path = CreatePcapWithModbusFrame(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A });

            try
            {
                var service = new PcapImportService(NullLogger<PcapImportService>.Instance);
                var result = service.Import(path);

                Assert.True(result.Success);
                var frame = Assert.Single(result.Frames);
                Assert.Equal(1, frame.UnitId);
                Assert.Equal(3, frame.FunctionCode);
                Assert.Equal(FrameDirection.Tx, frame.Direction);
                Assert.True(frame.DeltaMs < double.Epsilon);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Import_SkipsNonModbusTraffic()
        {
            var httpPayload = new byte[] { 0x47, 0x45, 0x54, 0x20, 0x2F, 0x20 };
            var path = CreatePcapWithTcpPayload(80, 8080, httpPayload);

            try
            {
                var service = new PcapImportService(NullLogger<PcapImportService>.Instance);
                var result = service.Import(path);

                Assert.False(result.Success);
                Assert.Empty(result.Frames);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Import_ExtractsModbusTcpFrameFromPcapNg()
        {
            var payload = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
            var packet = BuildTcpPacketBytes(54321, PcapImportService.ModbusTcpPort, payload);
            var path = CreatePcapNgFile(packet, useSpb: false);

            try
            {
                var service = new PcapImportService(NullLogger<PcapImportService>.Instance);
                var result = service.Import(path);

                Assert.True(result.Success);
                var frame = Assert.Single(result.Frames);
                Assert.Equal(1, frame.UnitId);
                Assert.Equal(3, frame.FunctionCode);
                Assert.Equal(FrameDirection.Tx, frame.Direction);
                // The EPB timestamp in the fixture is 1s after the epoch.
                Assert.Equal(1, (int)(frame.Timestamp.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Import_ExtractsModbusTcpFrameFromPcapNgSimplePacketBlock()
        {
            var payload = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
            var packet = BuildTcpPacketBytes(54321, PcapImportService.ModbusTcpPort, payload);
            var path = CreatePcapNgFile(packet, useSpb: true);

            try
            {
                var service = new PcapImportService(NullLogger<PcapImportService>.Instance);
                var result = service.Import(path);

                Assert.True(result.Success);
                var frame = Assert.Single(result.Frames);
                Assert.Equal(3, frame.FunctionCode);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Import_PcapNgWithoutInterfaceDescription_ReportsTheProblem()
        {
            var payload = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
            var packet = BuildTcpPacketBytes(54321, PcapImportService.ModbusTcpPort, payload);
            var path = CreatePcapNgFile(packet, includeIdb: false);

            try
            {
                var service = new PcapImportService(NullLogger<PcapImportService>.Instance);
                var result = service.Import(path);

                Assert.False(result.Success);
                Assert.Empty(result.Frames);
                Assert.Contains("link type", result.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Import_GarbageFile_ReportsItIsNotARecognizedCapture()
        {
            var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 1, 2, 3, 4, 5, 6, 7, 8 });

            try
            {
                var service = new PcapImportService(NullLogger<PcapImportService>.Instance);
                var result = service.Import(path);

                Assert.False(result.Success);
                Assert.Contains("recognized", result.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreatePcapWithModbusFrame(byte[] payload)
        {
            return CreatePcapWithTcpPayload(54321, PcapImportService.ModbusTcpPort, payload);
        }

        private static string CreatePcapWithTcpPayload(ushort srcPort, ushort dstPort, byte[] payload)
        {
            var packet = BuildTcpPacketBytes(srcPort, dstPort, payload);

            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(0xA1B2C3D4)); // magic (little-endian)
            bytes.AddRange(new byte[] { 2, 0, 4, 0 }); // major, minor
            bytes.AddRange(BitConverter.GetBytes(0)); // thiszone
            bytes.AddRange(BitConverter.GetBytes(0)); // sigfigs
            bytes.AddRange(BitConverter.GetBytes(65535)); // snaplen
            bytes.AddRange(BitConverter.GetBytes(1)); // link type Ethernet
            bytes.AddRange(BitConverter.GetBytes(0)); // ts_sec
            bytes.AddRange(BitConverter.GetBytes(0)); // ts_usec
            bytes.AddRange(BitConverter.GetBytes(packet.Length)); // incl_len
            bytes.AddRange(BitConverter.GetBytes(packet.Length)); // orig_len
            bytes.AddRange(packet);

            var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.pcap");
            File.WriteAllBytes(path, bytes.ToArray());
            return path;
        }

        /// <summary>
        /// Builds an Ethernet/IPv4/TCP packet (as raw bytes) carrying the payload.
        /// </summary>
        private static byte[] BuildTcpPacketBytes(ushort srcPort, ushort dstPort, byte[] payload)
        {
            var eth = new EthernetPacket(
                PhysicalAddress.Parse("00-00-00-00-00-00"),
                PhysicalAddress.Parse("00-00-00-00-00-00"),
                EthernetType.IPv4);

            var ip = new IPv4Packet(IPAddress.Parse("192.168.1.1"), IPAddress.Parse("192.168.1.2"));
            var tcp = new TcpPacket(srcPort, dstPort);
            tcp.PayloadData = payload;
            ip.PayloadPacket = tcp;
            eth.PayloadPacket = ip;

            return eth.Bytes;
        }

        /// <summary>
        /// Builds a minimal PCAPNG file: SHB + (optionally) IDB + one packet block
        /// (EPB with a 1-second timestamp, or SPB).
        /// </summary>
        private static string CreatePcapNgFile(byte[] packet, bool useSpb = false, bool includeIdb = true)
        {
            var output = new List<byte>();

            // Section Header Block: magic + version + section length (-1) + end of options.
            // The SHB is the one block with the type field first: [type][total][body][total].
            var shbBody = new byte[24];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(shbBody.AsSpan(0), 0x1A2B3C4D);
            shbBody[4] = 1; // version major
            for (var i = 8; i < 16; i++) shbBody[i] = 0xFF; // section length -1 (little-endian)
            // bytes 16..24: option code 0 (end of options), length 0
            var shbTotal = (uint)(12 + shbBody.Length);
            output.AddRange(Le32(0x0A0D0D0A)); // block type first for the SHB
            output.AddRange(Le32(shbTotal));
            output.AddRange(shbBody);
            output.AddRange(Le32(shbTotal));

            if (includeIdb)
            {
                // Interface Description Block: linktype 1 (Ethernet), reserved, snaplen, end of options.
                var idbBody = new byte[16];
                idbBody[0] = 1; // linktype Ethernet
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(idbBody.AsSpan(4), 65535);
                // bytes 8..16: option code 0, length 0
                WriteBlock(output, 0x00000001, idbBody);
            }

            if (useSpb)
            {
                // Simple Packet Block: original length + data (padded to 4).
                var padded = Pad4(packet);
                var spbBody = new byte[4 + padded.Length];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(spbBody.AsSpan(0), (uint)packet.Length);
                padded.CopyTo(spbBody.AsSpan(4));
                WriteBlock(output, 0x00000003, spbBody);
            }
            else
            {
                // Enhanced Packet Block: ifId, tsHi, tsLo (microseconds), captured, original, data, end of options.
                var padded = Pad4(packet);
                var body = new byte[20 + padded.Length + 8];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0), 0); // ifId
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0); // tsHi
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 1_000_000); // tsLo = 1s in microseconds
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), (uint)packet.Length);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(16), (uint)packet.Length);
                padded.CopyTo(body.AsSpan(20));
                // bytes (20+padded.Length)..+8: option code 0, length 0
                WriteBlock(output, 0x00000002, body);
            }

            var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.pcapng");
            File.WriteAllBytes(path, output.ToArray());
            return path;
        }

        private static void WriteBlock(List<byte> output, uint type, byte[] body)
        {
            var total = (uint)(12 + body.Length);
            output.AddRange(Le32(total));
            output.AddRange(Le32(type));
            output.AddRange(body);
            output.AddRange(Le32(total));
        }

        private static byte[] Le32(uint value)
        {
            var bytes = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(), value);
            return bytes;
        }

        private static byte[] Pad4(byte[] data)
        {
            var pad = (4 - (data.Length & 3)) & 3;
            if (pad == 0) return data;
            return data.Concat(Enumerable.Repeat((byte)0, pad)).ToArray();
        }
    }
}

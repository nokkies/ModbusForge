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

        private static string CreatePcapWithModbusFrame(byte[] payload)
        {
            return CreatePcapWithTcpPayload(54321, PcapImportService.ModbusTcpPort, payload);
        }

        private static string CreatePcapWithTcpPayload(ushort srcPort, ushort dstPort, byte[] payload)
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

            var packet = eth.Bytes;

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
    }
}

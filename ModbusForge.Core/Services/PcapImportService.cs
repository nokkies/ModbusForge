using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Tcp;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Imports a .pcap capture file, extracts Modbus TCP frames and turns them into <see cref="ModbusFrameLog"/> entries.
    /// </summary>
    public class PcapImportService
    {
        private readonly ILogger<PcapImportService> _logger;

        public const int ModbusTcpPort = 502;

        public PcapImportService(ILogger<PcapImportService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public PcapImportResult Import(string filePath)
        {
            var result = new PcapImportResult();

            try
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);

                var (magic, swapped) = ReadGlobalHeader(reader);
                if (magic != 0xA1B2C3D4 && magic != 0xA1B23C4D)
                {
                    result.Message = "Only little-endian pcap files are supported.";
                    _logger.LogWarning("Unsupported pcap magic {Magic:X8} in {FilePath}", magic, filePath);
                    return result;
                }

                if (magic == 0xA1B23C4D)
                {
                    result.Message = "Pcap files with nanosecond timestamps are not yet supported.";
                    _logger.LogWarning("Pcap nanosecond timestamps not supported in {FilePath}", filePath);
                    return result;
                }

                // Read the rest of the global header (version, timezone, sigfigs, snaplen, network)
                _ = reader.ReadBytes(20);

                var firstFrameTicks = 0L;
                var lastFrameTime = default(DateTime);

                while (stream.Position < stream.Length)
                {
                    var (timestamp, packetData) = ReadPacket(reader, swapped);
                    if (packetData is null || packetData.Length == 0)
                        break;

                    var frame = ParseModbusFrame(packetData, timestamp, ref firstFrameTicks, ref lastFrameTime);
                    if (frame is not null)
                        result.Frames.Add(frame);
                }

                result.Success = result.Frames.Count > 0;
                result.Message = result.Success
                    ? $"Imported {result.Frames.Count} Modbus TCP frames."
                    : "No Modbus TCP frames found on port 502.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Failed to import pcap file {FilePath}", filePath);
                result.Message = $"Failed to import pcap: {ex.Message}";
            }

            return result;
        }

        private static (uint Magic, bool Swapped) ReadGlobalHeader(BinaryReader reader)
        {
            var magic = reader.ReadUInt32();
            if (magic == 0xA1B2C3D4 || magic == 0xA1B23C4D)
                return (magic, false);

            // Try the reversed magic
            var reversed = BinaryPrimitives.ReverseEndianness(magic);
            if (reversed == 0xA1B2C3D4 || reversed == 0xA1B23C4D)
                return (reversed, true);

            return (magic, false);
        }

        private static (DateTime Timestamp, byte[]? Data) ReadPacket(BinaryReader reader, bool swapped)
        {
            try
            {
                var tsSec = reader.ReadUInt32();
                var tsUsec = reader.ReadUInt32();
                var inclLen = reader.ReadUInt32();
                _ = reader.ReadUInt32(); // orig_len

                if (swapped)
                {
                    tsSec = BinaryPrimitives.ReverseEndianness(tsSec);
                    tsUsec = BinaryPrimitives.ReverseEndianness(tsUsec);
                    inclLen = BinaryPrimitives.ReverseEndianness(inclLen);
                }

                if (inclLen > int.MaxValue || inclLen == 0)
                    return (DateTime.MinValue, null);

                var data = reader.ReadBytes((int)inclLen);
                var timestamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(tsSec)
                    .AddTicks(tsUsec * 10); // microseconds to 100ns ticks

                return (timestamp, data);
            }
            catch (EndOfStreamException)
            {
                return (DateTime.MinValue, null);
            }
        }

        private ModbusFrameLog? ParseModbusFrame(byte[] packetData, DateTime timestamp, ref long firstFrameTicks, ref DateTime lastFrameTime)
        {
            try
            {
                var packet = Packet.ParsePacket(LinkLayers.Ethernet, packetData);
                var ethernet = packet.Extract<EthernetPacket>();
                if (ethernet is null)
                    return null;

                var ip = ethernet.PayloadPacket as IPv4Packet;
                if (ip is null)
                    return null;

                var tcp = ip.PayloadPacket as TcpPacket;
                if (tcp is null || tcp.PayloadData is null || tcp.PayloadData.Length == 0)
                    return null;

                // Only consider Modbus TCP traffic on port 502
                if (tcp.SourcePort != ModbusTcpPort && tcp.DestinationPort != ModbusTcpPort)
                    return null;

                var payload = tcp.PayloadData;
                if (payload.Length < 8)
                    return null;

                // Modbus TCP MBAP header: Transaction (2), Protocol (2), Length (2), Unit (1), Function (1)
                var protocolId = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(payload, 2));
                if (protocolId != 0)
                    return null; // Not Modbus TCP

                var length = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(payload, 4));
                if (length + 6 > payload.Length)
                    return null;

                var unitId = payload[6];
                var functionCode = payload[7];

                var frame = new ModbusFrameLog
                {
                    Timestamp = timestamp.ToLocalTime(),
                    Direction = tcp.DestinationPort == ModbusTcpPort ? FrameDirection.Tx : FrameDirection.Rx,
                    RawBytes = payload.ToArray(),
                    UnitId = unitId,
                    FunctionCode = functionCode,
                };

                if (firstFrameTicks == 0)
                {
                    firstFrameTicks = timestamp.Ticks;
                    lastFrameTime = timestamp;
                }
                else
                {
                    frame.DeltaMs = (timestamp.Ticks - lastFrameTime.Ticks) / 10_000.0; // ticks to ms
                    lastFrameTime = timestamp;
                }

                return frame;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogDebug(ex, "Skipping non-Modbus or malformed packet");
                return null;
            }
        }
    }
}

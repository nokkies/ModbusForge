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
    /// Imports .pcap and .pcapng capture files, extracts Modbus TCP frames and
    /// turns them into <see cref="ModbusFrameLog"/> entries.
    /// </summary>
    public class PcapImportService
    {
        private readonly ILogger<PcapImportService> _logger;

        public const int ModbusTcpPort = 502;

        // Classic pcap global-header magics.
        private const uint PcapMagicLittleEndian = 0xA1B2C3D4;
        private const uint PcapMagicBigEndian = 0xD4C3B2A1;
        private const uint PcapMagicNanoseconds = 0xA1B23C4D;

        // PCAPNG: a file is a sequence of blocks, each
        // [total length (4, LE)][block type (4, LE)][block body][total length (4, LE)].
        // The file's first block is a Section Header Block, the one block whose
        // type field comes first ([type][total length]) - so the file's first
        // four bytes are 0x0A0D0D0A, the format's detection signature.
        private const uint PcapNgSectionHeaderBlock = 0x0A0D0D0A;
        private const uint PcapNgByteOrderMagic = 0x1A2B3C4D;
        private const uint PcapNgInterfaceDescriptionBlock = 0x00000001;
        private const uint PcapNgEnhancedPacketBlock = 0x00000002;
        private const uint PcapNgSimplePacketBlock = 0x00000003;
        private const int PcapNgBlockOverhead = 12; // total length + type + trailing total length

        // PCAPNG timestamps are counts of 10^-resolution seconds; 6 (microseconds)
        // is the spec default. The SHB "timestamp resolution" option (code 9)
        // can change it.
        private const int DefaultTimestampResolution = 6;
        private const int TsResolutionOptionCode = 9;
        private const int PcapNgOptionEnd = 0;
        private const int PcapNgOptionEndOfOptions = unchecked((int)0xFFFFFFFF);

        // Link-layer types this importer can parse.
        private const ushort LinkTypeEthernet = 1;
        private const ushort LinkTypeRawIPv4 = 101;

        private static readonly Dictionary<ushort, LinkLayers> SupportedLinkTypes = new()
        {
            [LinkTypeEthernet] = LinkLayers.Ethernet,
            [LinkTypeRawIPv4] = LinkLayers.IPv4,
        };

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

                if (stream.Length < 8)
                {
                    result.Message = "File is too small to be a capture.";
                    return result;
                }

                var first = reader.ReadUInt32();
                if (first == PcapNgSectionHeaderBlock)
                {
                    stream.Position = 0; // the PCAPNG reader consumes blocks from the start
                    ImportPcapNg(stream, reader, result, filePath);
                }
                else
                {
                    ImportPcap(stream, reader, result, filePath, first);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Failed to import pcap file {FilePath}", filePath);
                result.Message = $"Failed to import pcap: {ex.Message}";
            }

            return result;
        }

        private void ImportPcap(Stream stream, BinaryReader reader, PcapImportResult result, string filePath, uint firstMagic)
        {
            var (magic, swapped) = NormalizePcapMagic(firstMagic);
            if (magic == 0)
            {
                result.Message = "Not a recognized pcap or pcapng file.";
                _logger.LogWarning("Unsupported capture magic {Magic:X8} in {FilePath}", firstMagic, filePath);
                return;
            }

            if (magic == PcapMagicNanoseconds)
            {
                result.Message = "Pcap files with nanosecond timestamps are not yet supported.";
                _logger.LogWarning("Pcap nanosecond timestamps not supported in {FilePath}", filePath);
                return;
            }

            // The rest of the global header: version (4), timezone (4), sigfigs (4),
            // snaplen (4), link type (4).
            var headerRest = reader.ReadBytes(20);
            var linkTypeRaw = swapped
                ? (ushort)((headerRest[16] << 8) | headerRest[17])
                : (ushort)(headerRest[16] | (headerRest[17] << 8));

            if (!SupportedLinkTypes.TryGetValue(linkTypeRaw, out var linkLayer))
            {
                result.Message = $"Pcap link type {linkTypeRaw} is not supported (Ethernet and raw IPv4 are).";
                _logger.LogWarning("Unsupported pcap link type {LinkType} in {FilePath}", linkTypeRaw, filePath);
                return;
            }

            var firstFrameTicks = 0L;
            var lastFrameTime = default(DateTime);

            while (stream.Position < stream.Length)
            {
                var (timestamp, packetData) = ReadPacket(reader, swapped);
                if (packetData is null || packetData.Length == 0)
                    break;

                var frame = ParseModbusFrame(packetData, timestamp, linkLayer, ref firstFrameTicks, ref lastFrameTime);
                if (frame is not null)
                    result.Frames.Add(frame);
            }

            result.Success = result.Frames.Count > 0;
            result.Message = result.Success
                ? $"Imported {result.Frames.Count} Modbus TCP frames."
                : "No Modbus TCP frames found on port 502.";
        }

        private void ImportPcapNg(Stream stream, BinaryReader reader, PcapImportResult result, string filePath)
        {
            // The first block must be a Section Header Block. It is the only
            // block whose type field comes first: [type (4)][total length (4)]
            // instead of the usual [total length (4)][type (4)] - which is why
            // the file's first four bytes are the 0x0A0D0D0A detection magic.
            var shbType = reader.ReadUInt32();
            if (shbType != PcapNgSectionHeaderBlock)
            {
                result.Message = "Corrupt PCAPNG file: the first block is not a section header.";
                return;
            }

            var shbTotal = reader.ReadUInt32();
            if (shbTotal < 28 || (long)shbTotal > stream.Length - stream.Position + 8)
            {
                result.Message = "Corrupt PCAPNG file: invalid section header length.";
                return;
            }

            var shbBodyLength = (int)shbTotal - PcapNgBlockOverhead;
            var shbBody = reader.ReadBytes(shbBodyLength);
            if (shbBody.Length < shbBodyLength || stream.Length - stream.Position < 4)
            {
                result.Message = "Corrupt PCAPNG file: truncated section header.";
                return;
            }

            if (reader.ReadUInt32() != shbTotal)
            {
                result.Message = "Corrupt PCAPNG file: section header length mismatch.";
                return;
            }

            if (shbBody.Length < 12
                || BinaryPrimitives.ReadUInt32LittleEndian(shbBody.AsSpan()) != PcapNgByteOrderMagic)
            {
                result.Message = "Only little-endian PCAPNG files are supported.";
                return;
            }

            var timestampResolution = ReadTimestampResolution(shbBody);
            var linkTypesByInterface = new Dictionary<int, ushort>();
            var nextInterfaceId = 0;
            var firstFrameTicks = 0L;
            var lastFrameTime = default(DateTime);
            var packetBlocksSeen = 0;
            var unsupportedLinkType = false;

            while (stream.Position + PcapNgBlockOverhead <= stream.Length)
            {
                var totalLength = reader.ReadUInt32();
                var blockType = reader.ReadUInt32();

                // totalLength covers the whole block, including the repeated
                // length at its end. After the two 4-byte header fields, the
                // rest of the block is (totalLength - 8) bytes: the block
                // data plus the trailing length.
                if (totalLength < PcapNgBlockOverhead
                    || (long)totalLength - 8 > stream.Length - stream.Position)
                {
                    break; // corrupt or truncated block
                }

                var bodyLength = (int)totalLength - 8;
                var body = reader.ReadBytes(bodyLength);
                if (body.Length < bodyLength)
                    break;

                if (BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(bodyLength - 4)) != totalLength)
                    break; // trailing length disagrees - treat the file as corrupt

                switch (blockType)
                {
                    case PcapNgInterfaceDescriptionBlock:
                        if (body.Length >= 12)
                        {
                            linkTypesByInterface[nextInterfaceId] = (ushort)(body[0] | (body[1] << 8));
                            nextInterfaceId++;
                        }
                        break;

                    case PcapNgEnhancedPacketBlock:
                        if (body.Length < 24)
                            break;

                        packetBlocksSeen++;
                        var ifId = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan());
                        if (linkTypesByInterface.TryGetValue(ifId, out var epbLinkType)
                            && SupportedLinkTypes.TryGetValue(epbLinkType, out var epbLayer))
                        {
                            var tsHi = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4));
                            var tsLo = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8));
                            var capturedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12));
                            capturedLength = Math.Clamp(capturedLength, 0, body.Length - 20);
                            var epbTimestamp = DecodePcapNgTimestamp(tsHi, tsLo, timestampResolution);

                            // body: ifId(4) tsHi(4) tsLo(4) capturedLen(4) origLen(4) data ...
                            var frame = ParseModbusFrame(
                                body.AsSpan(20, capturedLength), epbTimestamp, epbLayer,
                                ref firstFrameTicks, ref lastFrameTime);
                            if (frame is not null)
                                result.Frames.Add(frame);
                        }
                        else
                        {
                            unsupportedLinkType = true; // unknown interface or unsupported link type
                        }
                        break;

                    case PcapNgSimplePacketBlock:
                        if (body.Length < 12)
                            break;

                        packetBlocksSeen++;
                        // SPBs carry no per-interface data; real captures use them
                        // only for single-interface sections, so the first
                        // described interface is the sensible choice.
                        if (linkTypesByInterface.TryGetValue(0, out var spbLinkType)
                            && SupportedLinkTypes.TryGetValue(spbLinkType, out var spbLayer))
                        {
                            var spbTimestamp = lastFrameTime == default ? DateTime.UnixEpoch : lastFrameTime;
                            var spbFrame = ParseModbusFrame(
                                body.AsSpan(4, body.Length - 4 - 4), spbTimestamp, spbLayer,
                                ref firstFrameTicks, ref lastFrameTime);
                            if (spbFrame is not null)
                                result.Frames.Add(spbFrame);
                        }
                        else
                        {
                            unsupportedLinkType = true;
                        }
                        break;

                    default:
                        // DEBs, block options, etc. - nothing to import.
                        break;
                }
            }

            result.Success = result.Frames.Count > 0;
            result.Message = result.Success
                ? $"Imported {result.Frames.Count} Modbus TCP frames."
                : unsupportedLinkType
                    ? "No Modbus TCP frames found; the capture uses a link type that is not supported (Ethernet and raw IPv4 are)."
                    : packetBlocksSeen == 0
                        ? "No packet blocks found in the PCAPNG file."
                        : "No Modbus TCP frames found on port 502.";
        }

        /// <summary>
        /// Reads the SHB "timestamp resolution" option (code 9); 6 (microseconds)
        /// when absent, per the spec default.
        /// </summary>
        private static int ReadTimestampResolution(byte[] shbBody)
        {
            var position = 12; // after magic, version major/minor, section length
            while (position + 8 <= shbBody.Length)
            {
                var code = (int)BinaryPrimitives.ReadUInt32LittleEndian(shbBody.AsSpan(position));
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(shbBody.AsSpan(position + 4));
                position += 8;

                if (code == PcapNgOptionEnd || code == PcapNgOptionEndOfOptions)
                    break;
                if (length < 0 || position + length > shbBody.Length)
                    break; // malformed option - fall back to the default

                if (code == TsResolutionOptionCode && length == 4)
                    return (int)BinaryPrimitives.ReadUInt32LittleEndian(shbBody.AsSpan(position));

                position += length + ((4 - (length & 3)) & 3); // option data + padding
            }

            return DefaultTimestampResolution;
        }

        private static DateTime DecodePcapNgTimestamp(uint hi, uint lo, int resolution)
        {
            // The timestamp is a (hi:lo) 64-bit count of 10^-resolution seconds.
            if (resolution == DefaultTimestampResolution)
            {
                var micros = (long)hi * 4_294_967_296L + lo;
                return DateTime.UnixEpoch.AddMicroseconds(micros);
            }

            var seconds = ((double)(long)hi * 4_294_967_296.0 + lo) * Math.Pow(10, -resolution);
            var wholeSeconds = (long)seconds;
            return DateTimeOffset.FromUnixTimeSeconds(wholeSeconds).UtcDateTime.AddSeconds(seconds - wholeSeconds);
        }

        private static (uint Magic, bool Swapped) NormalizePcapMagic(uint magic)
        {
            if (magic == PcapMagicLittleEndian || magic == PcapMagicNanoseconds)
                return (magic, false);

            if (magic == PcapMagicBigEndian)
                return (PcapMagicLittleEndian, true);

            var reversed = BinaryPrimitives.ReverseEndianness(magic);
            if (reversed == PcapMagicLittleEndian || reversed == PcapMagicNanoseconds)
                return (reversed, true);

            return (0, false);
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
                if (data.Length < inclLen)
                    return (DateTime.MinValue, null); // truncated final packet

                var timestamp = DateTime.UnixEpoch.AddSeconds(tsSec).AddTicks(tsUsec * 10);

                return (timestamp, data);
            }
            catch (EndOfStreamException)
            {
                return (DateTime.MinValue, null);
            }
        }

        private ModbusFrameLog? ParseModbusFrame(ReadOnlySpan<byte> packetData, DateTime timestamp, LinkLayers linkLayer, ref long firstFrameTicks, ref DateTime lastFrameTime)
        {
            try
            {
                var packet = Packet.ParsePacket(linkLayer, packetData.ToArray());
                var linkPacket = linkLayer == LinkLayers.Ethernet
                    ? (Packet?)packet.Extract<EthernetPacket>()
                    : packet;
                if (linkPacket is null)
                    return null;

                var ip = linkPacket.PayloadPacket as IPv4Packet;
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

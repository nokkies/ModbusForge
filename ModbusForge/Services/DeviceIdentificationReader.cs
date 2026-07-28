using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    /// <summary>
    /// Vendor, product and revision strings reported by FC43 / MEI type 14.
    /// </summary>
    public class ScannedDeviceIdentification
    {
        public string VendorName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
    }

    /// <summary>
    /// Reads FC43 (Read Device Identification) from a Modbus TCP unit.
    /// </summary>
    public interface IDeviceIdentificationReader
    {
        Task<ScannedDeviceIdentification?> ReadAsync(
            string ipAddress,
            int port,
            byte unitId,
            int connectTimeoutMs,
            int responseTimeoutMs,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// FC43 is not implemented by NModbus4, so the request is framed and parsed directly
    /// over a short-lived socket that is independent of the scan's Modbus master.
    /// </summary>
    public class DeviceIdentificationReader : IDeviceIdentificationReader
    {
        private const byte FunctionCode = 0x2B;
        private const byte MeiTypeDeviceIdentification = 0x0E;
        private const byte ReadDeviceIdBasic = 0x01;
        private const byte ObjectIdVendorName = 0x00;
        private const byte ObjectIdProductCode = 0x01;
        private const byte ObjectIdRevision = 0x02;
        private const int MbapHeaderLength = 7;
        private const int MaxResponseLength = 260;

        private readonly ILogger<DeviceIdentificationReader> _logger;

        public DeviceIdentificationReader(ILogger<DeviceIdentificationReader> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ScannedDeviceIdentification?> ReadAsync(
            string ipAddress,
            int port,
            byte unitId,
            int connectTimeoutMs,
            int responseTimeoutMs,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var tcpClient = new TcpClient();
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectTimeout.CancelAfter(Math.Max(1, connectTimeoutMs));
                    await tcpClient.ConnectAsync(ipAddress, port, connectTimeout.Token).ConfigureAwait(false);
                }

                var stream = tcpClient.GetStream();
                await stream.WriteAsync(BuildRequest(unitId), cancellationToken).ConfigureAwait(false);

                using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                responseTimeout.CancelAfter(Math.Max(1, responseTimeoutMs));

                var header = await ReadExactlyAsync(stream, MbapHeaderLength, responseTimeout.Token).ConfigureAwait(false);
                if (header == null)
                {
                    return null;
                }

                var remaining = ((header[4] << 8) | header[5]) - 1;
                if (remaining is <= 0 or > MaxResponseLength)
                {
                    return null;
                }

                var pdu = await ReadExactlyAsync(stream, remaining, responseTimeout.Token).ConfigureAwait(false);
                return pdu == null ? null : Parse(pdu);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "FC43 identification of {Ip}:{Port} unit {UnitId} failed", ipAddress, port, unitId);
                return null;
            }
        }

        internal static byte[] BuildRequest(byte unitId)
        {
            return new byte[]
            {
                0x00, 0x01,             // transaction id
                0x00, 0x00,             // protocol id
                0x00, 0x05,             // length: unit id + 4 PDU bytes
                unitId,
                FunctionCode,
                MeiTypeDeviceIdentification,
                ReadDeviceIdBasic,
                ObjectIdVendorName
            };
        }

        /// <summary>
        /// Parses an FC43 response PDU (function code first). Returns null for exception
        /// responses and for frames that are not well-formed device identification data.
        /// </summary>
        internal static ScannedDeviceIdentification? Parse(byte[] pdu)
        {
            ArgumentNullException.ThrowIfNull(pdu);

            // functionCode, meiType, readDeviceIdCode, conformityLevel, moreFollows, nextObjectId, objectCount
            const int minimumLength = 7;
            if (pdu.Length < minimumLength || pdu[0] != FunctionCode || pdu[1] != MeiTypeDeviceIdentification)
            {
                return null;
            }

            var objectCount = pdu[6];
            var offset = minimumLength;
            var values = new Dictionary<byte, string>(objectCount);

            for (var i = 0; i < objectCount; i++)
            {
                if (offset + 2 > pdu.Length)
                {
                    break;
                }

                var objectId = pdu[offset];
                var length = pdu[offset + 1];
                offset += 2;

                if (offset + length > pdu.Length)
                {
                    break;
                }

                values[objectId] = Encoding.ASCII.GetString(pdu, offset, length);
                offset += length;
            }

            if (values.Count == 0)
            {
                return null;
            }

            return new ScannedDeviceIdentification
            {
                VendorName = values.TryGetValue(ObjectIdVendorName, out var vendor) ? vendor : string.Empty,
                ProductCode = values.TryGetValue(ObjectIdProductCode, out var product) ? product : string.Empty,
                Revision = values.TryGetValue(ObjectIdRevision, out var revision) ? revision : string.Empty
            };
        }

        private static async Task<byte[]?> ReadExactlyAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
        {
            var buffer = new byte[count];
            var read = 0;

            while (read < count)
            {
                var chunk = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
                if (chunk == 0)
                {
                    return null;
                }

                read += chunk;
            }

            return buffer;
        }
    }
}

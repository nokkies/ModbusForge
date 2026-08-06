using System;
using System.Threading;
using System.Threading.Tasks;
using Modbus.Data;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Result of a connection diagnostic test
    /// </summary>
    public class ConnectionDiagnosticResult
    {
        public bool TcpConnected { get; set; }
        public bool ModbusResponding { get; set; }
        public string TcpError { get; set; } = string.Empty;
        public string ModbusError { get; set; } = string.Empty;
        public int TcpLatencyMs { get; set; }
        public int ModbusLatencyMs { get; set; }
        public string RemoteEndpoint { get; set; } = string.Empty;
        public string LocalEndpoint { get; set; } = string.Empty;
        public bool IsSerialConnection { get; set; }

        public bool IsFullyConnected => TcpConnected && ModbusResponding;

        public string Summary
        {
            get
            {
                if (IsFullyConnected)
                {
                    if (IsSerialConnection)
                        return $"✓ Serial connected - Modbus: {ModbusLatencyMs}ms";
                    return $"✓ Connected - TCP: {TcpLatencyMs}ms, Modbus: {ModbusLatencyMs}ms";
                }

                if (!TcpConnected)
                {
                    if (IsSerialConnection)
                        return $"✗ Serial Failed: {TcpError}";
                    return $"✗ TCP Failed: {TcpError}";
                }

                return IsSerialConnection
                    ? $"✓ Serial OK ({RemoteEndpoint}) | ✗ Modbus Failed: {ModbusError}"
                    : $"✓ TCP OK ({TcpLatencyMs}ms) | ✗ Modbus Failed: {ModbusError}";
            }
        }
    }

    public interface IModbusService : IDisposable, IAsyncDisposable
    {
        bool IsConnected { get; }
        string BoundEndpoint { get; }

        /// <summary>
        /// Captured Modbus frame log for this connection.
        /// </summary>
        ModbusFrameLogger FrameLogger { get; }

        // For client compatibility, but not used in server mode
        Task<bool> ConnectAsync(string ipAddress, int port, string unitIds = "1", CancellationToken cancellationToken = default);
        Task DisconnectAsync();

        /// <summary>
        /// Connects using a full connection profile. TCP/serial implementations may override this;
        /// the default delegates to the IP/port overload for backward compatibility.
        /// </summary>
        Task<bool> ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(profile);

            return ConnectAsync(profile.IpAddress, profile.Port, profile.UnitId.ToString(), cancellationToken);
        }

        /// <summary>
        /// Run connection diagnostics to identify where connection fails
        /// </summary>
        Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId);

        // Modbus operations
        Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count);
        Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count);
        Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value);
        Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values);

        // Coil operations
        Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count);
        Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count);
        Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value);

        // Advanced function codes
        /// <summary>
        /// FC22 (0x16) Mask Write Register: result = (current AND andMask) OR (orMask AND NOT andMask).
        /// Returns the resulting register value, or null when the operation could not be performed.
        /// </summary>
        Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask);

        /// <summary>
        /// FC23 (0x17) Read/Write Multiple Registers: performs the write first, then the read,
        /// in a single transaction. Returns the registers read, or null on failure.
        /// </summary>
        Task<ushort[]?> ReadWriteMultipleRegistersAsync(byte unitId, int readStartAddress, int readCount, int writeStartAddress, ushort[] writeValues);

        /// <summary>
        /// FC43/MEI 14 (0x2B) Read Device Identification, starting at <paramref name="objectId"/>.
        /// Returns the device identity objects, or null when the device does not answer /
        /// does not support the function.
        /// </summary>
        Task<DeviceIdentification?> ReadDeviceIdentificationAsync(byte unitId, byte objectId = DeviceIdObject.VendorName, DeviceIdCategory category = DeviceIdCategory.Basic);

        /// <summary>
        /// Returns the server's local data store when the service is running in server mode.
        /// Client implementations and unstarted services return null.
        /// </summary>
        DataStore? GetDataStore() => null;
    }
}

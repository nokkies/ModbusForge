using System;
using System.Threading.Tasks;

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
        
        public bool IsFullyConnected => TcpConnected && ModbusResponding;
        
        public string Summary
        {
            get
            {
                if (IsFullyConnected)
                    return $"✓ Connected - TCP: {TcpLatencyMs}ms, Modbus: {ModbusLatencyMs}ms";
                if (!TcpConnected)
                    return $"✗ TCP Failed: {TcpError}";
                return $"✓ TCP OK ({TcpLatencyMs}ms) | ✗ Modbus Failed: {ModbusError}";
            }
        }
    }

    public interface IModbusService : IDisposable
    {
        bool IsConnected { get; }
        
        // For client compatibility, but not used in server mode
        Task<bool> ConnectAsync(string ipAddress, int port);
        Task DisconnectAsync();
        
        /// <summary>
        /// Run connection diagnostics to identify where connection fails
        /// </summary>
        Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId);
        
        // Modbus operations
        Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count);
        Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count);
        Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value);

        // Coil operations
        Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count);
        Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count);
        Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value);
    }
}

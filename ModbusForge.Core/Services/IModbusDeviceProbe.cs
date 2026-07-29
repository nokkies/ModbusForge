using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Probes a single host for Modbus units, independently of the application's own connection.
    /// </summary>
    public interface IModbusDeviceProbe
    {
        /// <summary>
        /// Probes every unit ID in <paramref name="options"/> on <paramref name="ipAddress"/>:<paramref name="port"/>
        /// and, when requested, reads device identification and the configured register range from each unit that answers.
        /// </summary>
        Task<IReadOnlyList<DeviceScanResult>> ProbeHostAsync(
            string ipAddress,
            int port,
            DeviceScanOptions options,
            CancellationToken cancellationToken = default);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Scans an IP range for Modbus devices, sweeping unit IDs and optionally register ranges.
    /// </summary>
    public interface IDeviceScannerService
    {
        /// <summary>
        /// Validates <paramref name="options"/> and returns the first problem found, or null when valid.
        /// </summary>
        string? Validate(DeviceScanOptions options);

        /// <summary>
        /// Runs a scan, reporting each responding unit through <paramref name="deviceFound"/> as it is discovered.
        /// </summary>
        /// <exception cref="ArgumentException">The options are invalid.</exception>
        Task<IReadOnlyList<DeviceScanResult>> ScanAsync(
            DeviceScanOptions options,
            IProgress<DeviceScanProgress>? progress = null,
            Action<DeviceScanResult>? deviceFound = null,
            CancellationToken cancellationToken = default);
    }
}

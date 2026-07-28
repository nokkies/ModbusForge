using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Helpers;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Orchestrates a bounded-concurrency sweep of an IP range, delegating the actual
    /// Modbus traffic to an <see cref="IModbusDeviceProbe"/>.
    /// </summary>
    public class DeviceScannerService : IDeviceScannerService
    {
        public const int MaxConcurrency = 64;
        public const int MaxRegisterScanCount = 2000;
        public const int MaxPorts = 64;
        public const int MaxTargets = 8192;

        private readonly IModbusDeviceProbe _probe;
        private readonly ILogger<DeviceScannerService> _logger;

        public DeviceScannerService(IModbusDeviceProbe probe, ILogger<DeviceScannerService> logger)
        {
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string? Validate(DeviceScanOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return ValidateAddressRange(options, out var addressCount)
                ?? ValidatePortRange(options, addressCount)
                ?? ValidateUnitIds(options)
                ?? ValidateProbe(options)
                ?? ValidateRegisterRange(options);
        }

        private static string? ValidateAddressRange(DeviceScanOptions options, out long addressCount)
        {
            addressCount = 0;

            if (!IpRangeHelper.TryParseIPv4(options.StartIpAddress, out var start))
            {
                return $"'{options.StartIpAddress}' is not a valid IPv4 address.";
            }

            if (!IpRangeHelper.TryParseIPv4(options.EndIpAddress, out var end))
            {
                return $"'{options.EndIpAddress}' is not a valid IPv4 address.";
            }

            if (end < start)
            {
                return "The end IP address must not be lower than the start IP address.";
            }

            addressCount = end - start + 1L;
            return addressCount > IpRangeHelper.MaxAddresses
                ? $"The IP range covers {addressCount} addresses; at most {IpRangeHelper.MaxAddresses} are allowed."
                : null;
        }

        private static string? ValidatePortRange(DeviceScanOptions options, long addressCount)
        {
            if (options.StartPort is < 1 or > 65535 || options.EndPort is < 1 or > 65535)
            {
                return "Ports must be between 1 and 65535.";
            }

            if (options.EndPort < options.StartPort)
            {
                return "The end port must not be lower than the start port.";
            }

            var portCount = options.EndPort - options.StartPort + 1;
            if (portCount > MaxPorts)
            {
                return $"The port range covers {portCount} ports; at most {MaxPorts} are allowed.";
            }

            return addressCount * portCount > MaxTargets
                ? $"The scan would probe more than {MaxTargets} endpoints; narrow the IP or port range."
                : null;
        }

        private static string? ValidateUnitIds(DeviceScanOptions options)
        {
            if (options.StartUnitId < DeviceScanOptions.MinUnitId || options.EndUnitId > DeviceScanOptions.MaxUnitId)
            {
                return $"Unit IDs must be between {DeviceScanOptions.MinUnitId} and {DeviceScanOptions.MaxUnitId}.";
            }

            return options.EndUnitId < options.StartUnitId
                ? "The end unit ID must not be lower than the start unit ID."
                : null;
        }

        private static string? ValidateProbe(DeviceScanOptions options)
        {
            if (options.ConnectTimeoutMs < 1 || options.ResponseTimeoutMs < 1)
            {
                return "Timeouts must be at least 1 ms.";
            }

            return options.ProbeAddress is < 0 or > ushort.MaxValue
                ? $"The probe address must be between 0 and {ushort.MaxValue}."
                : null;
        }

        private static string? ValidateRegisterRange(DeviceScanOptions options)
        {
            if (!options.ScanRegisterRange)
            {
                return null;
            }

            if (options.RegisterScanStartAddress is < 0 or > ushort.MaxValue)
            {
                return $"The register scan start address must be between 0 and {ushort.MaxValue}.";
            }

            if (options.RegisterScanCount is < 1 or > MaxRegisterScanCount)
            {
                return $"The register scan count must be between 1 and {MaxRegisterScanCount}.";
            }

            return options.RegisterScanStartAddress + options.RegisterScanCount - 1 > ushort.MaxValue
                ? "The register scan range extends past address 65535."
                : null;
        }

        public async Task<IReadOnlyList<DeviceScanResult>> ScanAsync(
            DeviceScanOptions options,
            IProgress<DeviceScanProgress>? progress = null,
            Action<DeviceScanResult>? deviceFound = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            var validationError = Validate(options);
            if (validationError != null)
            {
                throw new ArgumentException(validationError, nameof(options));
            }

            var hosts = IpRangeHelper.Expand(options.StartIpAddress, options.EndIpAddress);
            var targets = (
                from host in hosts
                from port in Enumerable.Range(options.StartPort, options.EndPort - options.StartPort + 1)
                select (Host: host, Port: port)).ToList();
            var concurrency = Math.Clamp(options.MaxConcurrency, 1, MaxConcurrency);
            var results = new List<DeviceScanResult>();
            var resultsLock = new object();
            var completed = 0;
            var devicesFound = 0;

            _logger.LogInformation(
                "Scanning {TargetCount} endpoint(s) across {HostCount} host(s), ports {StartPort}-{EndPort}, unit IDs {StartUnitId}-{EndUnitId}",
                targets.Count, hosts.Count, options.StartPort, options.EndPort, options.StartUnitId, options.EndUnitId);

            using var throttle = new SemaphoreSlim(concurrency, concurrency);

            var tasks = targets.Select(async target =>
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var hostResults = await _probe.ProbeHostAsync(target.Host, target.Port, options, cancellationToken).ConfigureAwait(false);

                    lock (resultsLock)
                    {
                        foreach (var hostResult in hostResults)
                        {
                            results.Add(hostResult);
                            if (hostResult.IsDevice)
                            {
                                devicesFound++;
                                deviceFound?.Invoke(hostResult);
                            }
                        }

                        completed++;
                        progress?.Report(new DeviceScanProgress
                        {
                            Completed = completed,
                            Total = targets.Count,
                            CurrentTarget = $"{target.Host}:{target.Port}",
                            DevicesFound = devicesFound
                        });
                    }
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return results
                .OrderBy(r => IpRangeHelper.TryParseIPv4(r.IpAddress, out var value) ? value : uint.MaxValue)
                .ThenBy(r => r.Port)
                .ThenBy(r => r.UnitId)
                .ToList();
        }
    }
}

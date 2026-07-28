using System.Collections.Generic;

namespace ModbusForge.Models
{
    /// <summary>
    /// Modbus register space probed by the device scanner.
    /// </summary>
    public enum ScanRegisterType
    {
        HoldingRegisters,
        InputRegisters,
        Coils,
        DiscreteInputs
    }

    /// <summary>
    /// How a probed device answered a Modbus request.
    /// </summary>
    public enum DeviceProbeStatus
    {
        /// <summary>No TCP connection could be established.</summary>
        NoTcpConnection,

        /// <summary>TCP succeeded but the unit did not answer the Modbus request.</summary>
        NoModbusResponse,

        /// <summary>The unit answered with a valid Modbus response.</summary>
        Responded,

        /// <summary>The unit answered with a Modbus exception, which still proves it exists.</summary>
        RespondedWithException
    }

    /// <summary>
    /// Parameters for a device scan across an IP range, a unit ID range and an optional register range.
    /// </summary>
    public class DeviceScanOptions
    {
        public const byte MinUnitId = 1;
        public const byte MaxUnitId = 247;

        public string StartIpAddress { get; set; } = "127.0.0.1";
        public string EndIpAddress { get; set; } = "127.0.0.1";
        public int StartPort { get; set; } = 502;
        public int EndPort { get; set; } = 502;
        public byte StartUnitId { get; set; } = MinUnitId;
        public byte EndUnitId { get; set; } = MinUnitId;
        public int ConnectTimeoutMs { get; set; } = 500;
        public int ResponseTimeoutMs { get; set; } = 1000;

        /// <summary>Number of hosts probed in parallel.</summary>
        public int MaxConcurrency { get; set; } = 16;

        /// <summary>Register space and address used for the discovery probe.</summary>
        public ScanRegisterType RegisterType { get; set; } = ScanRegisterType.HoldingRegisters;
        public int ProbeAddress { get; set; }

        /// <summary>When true, discovered units are additionally scanned over <see cref="RegisterScanCount"/> addresses.</summary>
        public bool ScanRegisterRange { get; set; }
        public int RegisterScanStartAddress { get; set; }
        public int RegisterScanCount { get; set; } = 16;

        /// <summary>Addresses read per Modbus request while scanning a register range.</summary>
        public int RegisterScanBlockSize { get; set; } = 8;

        /// <summary>When true, FC43 Read Device Identification is issued against each discovered unit.</summary>
        public bool ReadDeviceIdentification { get; set; } = true;
    }

    /// <summary>
    /// A single address read while scanning a register range.
    /// </summary>
    public class RegisterScanResult
    {
        public int Address { get; set; }
        public bool IsReadable { get; set; }
        public ushort Value { get; set; }
        public string Error { get; set; } = string.Empty;

        public string DisplayValue => IsReadable ? Value.ToString() : "-";
    }

    /// <summary>
    /// Outcome of probing one unit ID on one host.
    /// </summary>
    public class DeviceScanResult
    {
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public byte UnitId { get; set; }
        public DeviceProbeStatus Status { get; set; }
        public int LatencyMs { get; set; }
        public string Message { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public List<RegisterScanResult> Registers { get; } = new();

        /// <summary>Vendor/product/revision as reported by FC43, or an empty string when unavailable.</summary>
        public string Identification
        {
            get
            {
                var parts = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(VendorName)) parts.Add(VendorName);
                if (!string.IsNullOrWhiteSpace(ProductCode)) parts.Add(ProductCode);
                if (!string.IsNullOrWhiteSpace(Revision)) parts.Add(Revision);
                return string.Join(" | ", parts);
            }
        }

        /// <summary>True when the unit answered, whether normally or with a Modbus exception.</summary>
        public bool IsDevice => Status is DeviceProbeStatus.Responded or DeviceProbeStatus.RespondedWithException;

        public string Endpoint => $"{IpAddress}:{Port}";
    }

    /// <summary>
    /// Progress of a running scan.
    /// </summary>
    public class DeviceScanProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string CurrentTarget { get; set; } = string.Empty;
        public int DevicesFound { get; set; }

        public double PercentComplete => Total <= 0 ? 0 : (double)Completed / Total * 100.0;
    }
}

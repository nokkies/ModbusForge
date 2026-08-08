using System.Collections.Generic;
using System.Linq;

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

        /// <summary>Register space and address used for the discovery probe. Addresses are 1-based display addresses (0 maps to protocol address 0).</summary>
        public ScanRegisterType RegisterType { get; set; } = ScanRegisterType.HoldingRegisters;
        public int ProbeAddress { get; set; } = 1;

        /// <summary>When true, discovered units are additionally scanned over <see cref="RegisterScanCount"/> addresses.</summary>
        public bool ScanRegisterRange { get; set; }

        /// <summary>Start of the register scan range. 1-based display address (0 maps to protocol address 0).</summary>
        public int RegisterScanStartAddress { get; set; } = 1;
        public int RegisterScanCount { get; set; } = 16;

        /// <summary>Addresses read per Modbus request while scanning a register range.</summary>
        public int RegisterScanBlockSize { get; set; } = 8;

        /// <summary>When true, FC43 Read Device Identification is issued against each discovered unit.</summary>
        public bool ReadDeviceIdentification { get; set; } = true;

        /// <summary>
        /// When true, each discovered unit is read once per register space so the scan can
        /// report which of FC01/FC02/FC03/FC04 the unit implements.
        /// </summary>
        public bool DetectFunctionCodes { get; set; } = true;
    }

    /// <summary>
    /// Read function codes the scanner can attribute to a register space.
    /// </summary>
    public static class ScanFunctionCode
    {
        public const byte ReadCoils = 1;
        public const byte ReadDiscreteInputs = 2;
        public const byte ReadHoldingRegisters = 3;
        public const byte ReadInputRegisters = 4;

        public static byte For(ScanRegisterType registerType) => registerType switch
        {
            ScanRegisterType.Coils => ReadCoils,
            ScanRegisterType.DiscreteInputs => ReadDiscreteInputs,
            ScanRegisterType.InputRegisters => ReadInputRegisters,
            _ => ReadHoldingRegisters
        };
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

        /// <summary>Read function codes the unit implements, as detected by the scan.</summary>
        public List<byte> SupportedFunctionCodes { get; } = new();

        public string SupportedFunctionCodesText => SupportedFunctionCodes.Count == 0
            ? string.Empty
            : string.Join(", ", SupportedFunctionCodes.Order().Select(fc => $"FC{fc:00}"));

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

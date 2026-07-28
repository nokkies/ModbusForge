using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using TransportType = ModbusForge.Models.TransportType;

namespace ModbusForge.Services
{
    /// <summary>
    /// Provides validation for user inputs and connection parameters.
    /// </summary>
    public interface IValidationService
    {
        ValidationResult ValidateIpAddress(string ipAddress);
        ValidationResult ValidatePort(int port);
        ValidationResult ValidateUnitId(byte unitId);
        ValidationResult ValidateAddress(int address);
        ValidationResult ValidateRegisterCount(int count);
        ValidationResult ValidateConnectionString(string connectionString);
        ValidationResult ValidateModbusValue(ushort value);
        ValidationResult ValidateCoilValue(bool value);
        ValidationResult ValidateSerialPort(string portName);
        ValidationResult ValidateBaudRate(int baudRate);
        ValidationResult ValidateDataBits(int dataBits);
        ValidationResult ValidateStopBits(StopBits stopBits);
        ValidationResult ValidateParity(Parity parity);
        ValidationResult ValidateSerialSettings(ConnectionProfile profile);
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }

        public static ValidationResult Success => new ValidationResult { IsValid = true };

        public static ValidationResult Failure(string message, string? details = null)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = message,
                ErrorDetails = details
            };
        }
    }

    public class ValidationService : IValidationService
    {
        private readonly ILogger<ValidationService> _logger;

        // Constants for validation
        private const int MinPort = 1;
        private const int MaxPort = 65535;
        private const int MinAddress = 0;
        private const int MaxAddress = 65535;
        private const int MinRegisterCount = 1;
        private const int MaxRegisterCount = 125; // Modbus standard limit
        private const ushort MaxModbusValue = 65535;

        private const int MinDataBits = 5;
        private const int MaxDataBits = 8;

        // Common baud rates; also accept any positive integer as a valid custom rate
        private static readonly HashSet<int> StandardBaudRates = new()
        {
            75, 110, 300, 600, 1200, 2400, 4800, 9600,
            14400, 19200, 38400, 57600, 115200, 128000,
            230400, 256000, 460800, 921600
        };

        public ValidationService(ILogger<ValidationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValidationResult ValidateIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return ValidationResult.Failure("IP address cannot be empty");
            }

            try
            {
                // Check for common special addresses
                if (ipAddress.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                    ipAddress.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationResult.Success;
                }

                if (!IPAddress.TryParse(ipAddress, out var address))
                {
                    return ValidationResult.Failure($"Invalid IP address format: {ipAddress}");
                }

                // Validate address family
                if (address.AddressFamily != AddressFamily.InterNetwork &&
                    address.AddressFamily != AddressFamily.InterNetworkV6)
                {
                    return ValidationResult.Failure($"Unsupported address family: {address.AddressFamily}");
                }

                return ValidationResult.Success;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error validating IP address: {IpAddress}", ipAddress);
                return ValidationResult.Failure($"Error validating IP address: {ex.Message}");
            }
        }

        public ValidationResult ValidatePort(int port)
        {
            if (port < MinPort || port > MaxPort)
            {
                return ValidationResult.Failure(
                    $"Port must be between {MinPort} and {MaxPort}",
                    $"Provided port: {port}");
            }

            // Warn about well-known ports that might require admin privileges
            if (port < 1024)
            {
                _logger.LogWarning("Port {Port} is a well-known port and may require administrator privileges", port);
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateUnitId(byte unitId)
        {
            // Unit ID 0 is broadcast, which might not be supported by all devices
            if (unitId == 0)
            {
                _logger.LogWarning("Unit ID 0 is broadcast address and may not be supported by all devices");
            }

            // Unit ID 247-255 are reserved
            if (unitId >= 247 && unitId <= 255)
            {
                return ValidationResult.Failure(
                    $"Unit ID {unitId} is reserved (247-255 are reserved for special purposes)");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateAddress(int address)
        {
            if (address < MinAddress || address > MaxAddress)
            {
                return ValidationResult.Failure(
                    $"Address must be between {MinAddress} and {MaxAddress}",
                    $"Provided address: {address}");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateRegisterCount(int count)
        {
            if (count < MinRegisterCount)
            {
                return ValidationResult.Failure(
                    $"Register count must be at least {MinRegisterCount}",
                    $"Provided count: {count}");
            }

            if (count > MaxRegisterCount)
            {
                return ValidationResult.Failure(
                    $"Register count cannot exceed {MaxRegisterCount} (Modbus standard limit)",
                    $"Provided count: {count}");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return ValidationResult.Failure("Connection string cannot be empty");
            }

            // Expected format: "ip:port" or "ip:port:unitId"
            var parts = connectionString.Split(':');

            if (parts.Length < 2 || parts.Length > 3)
            {
                return ValidationResult.Failure(
                    "Invalid connection string format. Expected: 'ip:port' or 'ip:port:unitId'",
                    $"Provided: {connectionString}");
            }

            var ipResult = ValidateIpAddress(parts[0]);
            if (!ipResult.IsValid)
            {
                return ValidationResult.Failure(
                    $"Invalid IP address in connection string: {ipResult.ErrorMessage}",
                    connectionString);
            }

            if (!int.TryParse(parts[1], out var port))
            {
                return ValidationResult.Failure(
                    $"Invalid port number in connection string: {parts[1]}",
                    connectionString);
            }

            var portResult = ValidatePort(port);
            if (!portResult.IsValid)
            {
                return ValidationResult.Failure(
                    $"Invalid port in connection string: {portResult.ErrorMessage}",
                    connectionString);
            }

            if (parts.Length == 3)
            {
                if (!byte.TryParse(parts[2], out var unitId))
                {
                    return ValidationResult.Failure(
                        $"Invalid unit ID in connection string: {parts[2]}",
                        connectionString);
                }

                var unitIdResult = ValidateUnitId(unitId);
                if (!unitIdResult.IsValid)
                {
                    return ValidationResult.Failure(
                        $"Invalid unit ID in connection string: {unitIdResult.ErrorMessage}",
                        connectionString);
                }
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateModbusValue(ushort value)
        {
            if (value > MaxModbusValue)
            {
                return ValidationResult.Failure(
                    $"Modbus register value cannot exceed {MaxModbusValue}",
                    $"Provided value: {value}");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateCoilValue(bool value)
        {
            // Boolean values are always valid for coils
            return ValidationResult.Success;
        }

        public ValidationResult ValidateSerialPort(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return ValidationResult.Failure("COM port name cannot be empty");
            }

            if (!Regex.IsMatch(portName, @"^COM\d+$", RegexOptions.IgnoreCase))
            {
                return ValidationResult.Failure(
                    $"Invalid COM port name: {portName}",
                    "Expected format: COM1, COM2, ...");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateBaudRate(int baudRate)
        {
            if (baudRate <= 0)
            {
                return ValidationResult.Failure(
                    "Baud rate must be greater than zero",
                    $"Provided baud rate: {baudRate}");
            }

            if (!StandardBaudRates.Contains(baudRate))
            {
                _logger.LogWarning("Baud rate {BaudRate} is non-standard but will be accepted", baudRate);
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateDataBits(int dataBits)
        {
            if (dataBits < MinDataBits || dataBits > MaxDataBits)
            {
                return ValidationResult.Failure(
                    $"Data bits must be between {MinDataBits} and {MaxDataBits}",
                    $"Provided data bits: {dataBits}");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateStopBits(StopBits stopBits)
        {
            if (stopBits == StopBits.None)
            {
                return ValidationResult.Failure(
                    "Stop bits cannot be None",
                    "Valid values: One, OnePointFive, Two");
            }

            if (!Enum.IsDefined(typeof(StopBits), stopBits))
            {
                return ValidationResult.Failure($"Invalid stop bits value: {stopBits}");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateParity(Parity parity)
        {
            if (!Enum.IsDefined(typeof(Parity), parity))
            {
                return ValidationResult.Failure($"Invalid parity value: {parity}");
            }

            return ValidationResult.Success;
        }

        public ValidationResult ValidateSerialSettings(ConnectionProfile profile)
        {
            if (profile == null)
            {
                return ValidationResult.Failure("Connection profile cannot be null");
            }

            if (profile.Transport != TransportType.Rtu && profile.Transport != TransportType.Ascii)
            {
                return ValidationResult.Failure(
                    "Serial settings validation requested for non-serial transport",
                    $"Transport: {profile.Transport}");
            }

            var result = ValidateSerialPort(profile.ComPort);
            if (!result.IsValid) return result;

            result = ValidateBaudRate(profile.BaudRate);
            if (!result.IsValid) return result;

            result = ValidateDataBits(profile.DataBits);
            if (!result.IsValid) return result;

            result = ValidateParity(profile.Parity);
            if (!result.IsValid) return result;

            result = ValidateStopBits(profile.StopBits);
            if (!result.IsValid) return result;

            result = ValidateUnitId(profile.UnitId);
            if (!result.IsValid) return result;

            return ValidationResult.Success;
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Helpers;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Handles all register and coil operations including reading, writing, and data type conversions.
    /// </summary>
    public class RegisterCoordinator
    {
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly ILogger<RegisterCoordinator> _logger;

        public RegisterCoordinator(
            ModbusTcpService clientService,
            ModbusServerService serverService,
            IConsoleLoggerService consoleLoggerService,
            ILogger<RegisterCoordinator> logger)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the appropriate Modbus service based on mode.
        /// </summary>
        private IModbusService GetService(bool isServerMode) => isServerMode ? _serverService : _clientService;

        /// <summary>
        /// Reads holding registers and populates the collection with proper data type formatting.
        /// </summary>
        public async Task ReadRegistersAsync(byte unitId, int start, int count, string globalType,
            ObservableCollection<RegisterEntry> holdingRegisters, Action<string> setStatusMessage,
            Action<bool> setHasConnectionError, bool isMonitoringEnabled, bool isServerMode)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage("Reading registers...");
                _consoleLoggerService.Log($"Reading {count} holding registers from address {start}");
                
                var values = await service.ReadHoldingRegistersAsync(unitId, start, count);
                if (values is null)
                {
                    setStatusMessage("Failed to read registers (connection lost)");
                    return;
                }

                // Preserve per-address Type if rows already exist
                var typeByAddress = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var r in holdingRegisters)
                {
                    typeByAddress[r.Address] = r.Type ?? globalType;
                }

                holdingRegisters.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    int addr = start + i;
                    var entry = new RegisterEntry
                    {
                        Address = addr,
                        Value = values[i],
                        Type = typeByAddress.TryGetValue(addr, out var t) ? t : globalType
                    };
                    holdingRegisters.Add(entry);
                }

                // Compute ValueText based on Type for better display (floats, strings, signed ints)
                int idx = 0;
                while (idx < holdingRegisters.Count)
                {
                    var entry = holdingRegisters[idx];
                    var type = (entry.Type ?? "uint").ToLowerInvariant();
                    if (type == "int")
                    {
                        short sv = unchecked((short)values[idx]);
                        entry.ValueText = sv.ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                    }
                    else if (type == "real")
                    {
                        if (idx + 1 < values.Length)
                        {
                            float f = DataTypeConverter.ToSingle(values[idx], values[idx + 1]);
                            entry.ValueText = f.ToString(CultureInfo.InvariantCulture);
                            holdingRegisters[idx + 1].ValueText = string.Empty;
                        }
                        else
                        {
                            entry.ValueText = entry.Value.ToString(CultureInfo.InvariantCulture);
                        }
                        idx += 2;
                    }
                    else if (type == "string")
                    {
                        entry.ValueText = DataTypeConverter.ToString(values[idx]);
                        idx += 1;
                    }
                    else
                    {
                        entry.ValueText = entry.Value.ToString(CultureInfo.InvariantCulture);
                        idx += 1;
                    }
                }

                setStatusMessage($"Read {values.Length} registers");
                setHasConnectionError(false);
                _consoleLoggerService.Log($"Read {values.Length} registers");
            }
            catch (Exception ex)
            {
                setStatusMessage($"Error reading registers: {ex.Message}");
                _logger.LogError(ex, "Error reading registers");
                
                if (isMonitoringEnabled)
                {
                    MessageBox.Show($"Failed to read registers: {ex.Message}\n\nContinuous monitoring has been paused. Fix the issue and re-enable monitoring.", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to read registers: {ex.Message}", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Reads input registers and populates the collection.
        /// </summary>
        public async Task ReadInputRegistersAsync(byte unitId, int start, int count, string globalType,
            ObservableCollection<RegisterEntry> inputRegisters, Action<string> setStatusMessage,
            Action<bool> setHasConnectionError, bool isMonitoringEnabled, bool isServerMode)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage("Reading input registers...");
                _consoleLoggerService.Log($"Reading {count} input registers from address {start}");
                
                var values = await service.ReadInputRegistersAsync(unitId, start, count);
                if (values is null)
                {
                    setStatusMessage("Failed to read input registers (connection lost)");
                    return;
                }

                inputRegisters.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    inputRegisters.Add(new RegisterEntry
                    {
                        Address = start + i,
                        Value = values[i],
                        Type = globalType
                    });
                }

                setStatusMessage($"Read {values.Length} input registers");
                setHasConnectionError(false);
                _consoleLoggerService.Log($"Read {values.Length} input registers");
            }
            catch (Exception ex)
            {
                setStatusMessage($"Error reading input registers: {ex.Message}");
                _logger.LogError(ex, "Error reading input registers");
                
                if (isMonitoringEnabled)
                {
                    MessageBox.Show($"Failed to read input registers: {ex.Message}\n\nContinuous monitoring has been paused. Fix the issue and re-enable monitoring.", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to read input registers: {ex.Message}", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Writes a single register.
        /// </summary>
        public async Task WriteRegisterAsync(byte unitId, int address, ushort value, 
            Action<string> setStatusMessage, Func<Task> refreshRegisters, bool isServerMode)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage("Writing register...");
                _consoleLoggerService.Log($"Writing register {address} with value {value}");
                
                await service.WriteSingleRegisterAsync(unitId, address, value);
                setStatusMessage("Register written");
                _consoleLoggerService.Log("Register written");
                
                // Optionally refresh
                await refreshRegisters();
            }
            catch (Exception ex)
            {
                setStatusMessage($"Error writing register: {ex.Message}");
                _logger.LogError(ex, "Error writing register");
                _consoleLoggerService.Log($"Error writing register: {ex.Message}");
                MessageBox.Show($"Failed to write register: {ex.Message}", "Write Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Writes a single register value at a specific address (for inline editing).
        /// </summary>
        public async Task WriteRegisterAtAsync(byte unitId, int address, ushort value, bool isServerMode)
        {
            var service = GetService(isServerMode);
            await service.WriteSingleRegisterAsync(unitId, address, value);
        }

        /// <summary>
        /// Writes a float (REAL) value across two registers.
        /// </summary>
        public async Task WriteFloatAtAsync(byte unitId, int address, float value, bool isServerMode)
        {
            var service = GetService(isServerMode);
            var registers = DataTypeConverter.ToUInt16(value);
            await service.WriteSingleRegisterAsync(unitId, address, registers[0]);
            await service.WriteSingleRegisterAsync(unitId, address + 1, registers[1]);
        }

        /// <summary>
        /// Writes a string value to registers.
        /// </summary>
        public async Task WriteStringAtAsync(byte unitId, int address, string text, bool isServerMode)
        {
            var service = GetService(isServerMode);
            var registers = DataTypeConverter.ToUInt16(text);
            for (int i = 0; i < registers.Length; i++)
            {
                await service.WriteSingleRegisterAsync(unitId, address + i, registers[i]);
            }
        }

        /// <summary>
        /// Writes a coil value at a specific address.
        /// </summary>
        public async Task WriteCoilAtAsync(byte unitId, int address, bool state, bool isServerMode)
        {
            var service = GetService(isServerMode);
            await service.WriteSingleCoilAsync(unitId, address, state);
        }

        /// <summary>
        /// Reads coils and populates the collection.
        /// </summary>
        public async Task ReadCoilsAsync(byte unitId, int start, int count,
            ObservableCollection<CoilEntry> coils, Action<string> setStatusMessage,
            Action<bool> setHasConnectionError, bool isMonitoringEnabled, bool isServerMode)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage("Reading coils...");
                _consoleLoggerService.Log($"Reading {count} coils from address {start}");
                
                var states = await service.ReadCoilsAsync(unitId, start, count);
                if (states is null)
                {
                    setStatusMessage("Failed to read coils (connection lost)");
                    return;
                }

                coils.Clear();
                for (int i = 0; i < states.Length; i++)
                {
                    coils.Add(new CoilEntry
                    {
                        Address = start + i,
                        State = states[i]
                    });
                }

                setStatusMessage($"Read {states.Length} coils");
                setHasConnectionError(false);
                _consoleLoggerService.Log($"Read {states.Length} coils");
            }
            catch (Exception ex)
            {
                setStatusMessage($"Error reading coils: {ex.Message}");
                _logger.LogError(ex, "Error reading coils");
                
                if (isMonitoringEnabled)
                {
                    MessageBox.Show($"Failed to read coils: {ex.Message}\n\nContinuous monitoring has been paused. Fix the issue and re-enable monitoring.", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to read coils: {ex.Message}", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Reads discrete inputs and populates the collection.
        /// </summary>
        public async Task ReadDiscreteInputsAsync(byte unitId, int start, int count,
            ObservableCollection<CoilEntry> discreteInputs, Action<string> setStatusMessage,
            Action<bool> setHasConnectionError, bool isMonitoringEnabled, bool isServerMode)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage("Reading discrete inputs...");
                _consoleLoggerService.Log($"Reading {count} discrete inputs from address {start}");
                
                var states = await service.ReadDiscreteInputsAsync(unitId, start, count);
                if (states is null)
                {
                    setStatusMessage("Failed to read discrete inputs (connection lost)");
                    return;
                }

                discreteInputs.Clear();
                for (int i = 0; i < states.Length; i++)
                {
                    discreteInputs.Add(new CoilEntry
                    {
                        Address = start + i,
                        State = states[i]
                    });
                }

                setStatusMessage($"Read {states.Length} discrete inputs");
                setHasConnectionError(false);
                _consoleLoggerService.Log($"Read {states.Length} discrete inputs");
            }
            catch (Exception ex)
            {
                setStatusMessage($"Error reading discrete inputs: {ex.Message}");
                _logger.LogError(ex, "Error reading discrete inputs");
                
                if (isMonitoringEnabled)
                {
                    MessageBox.Show($"Failed to read discrete inputs: {ex.Message}\n\nContinuous monitoring has been paused. Fix the issue and re-enable monitoring.", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to read discrete inputs: {ex.Message}", "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Writes a single coil.
        /// </summary>
        public async Task WriteCoilAsync(byte unitId, int address, bool state,
            Action<string> setStatusMessage, Func<Task> refreshCoils, bool isServerMode)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage("Writing coil...");
                _consoleLoggerService.Log($"Writing coil {address} with value {(state ? 1 : 0)}");
                
                await service.WriteSingleCoilAsync(unitId, address, state);
                setStatusMessage("Coil written");
                _consoleLoggerService.Log("Coil written");
                
                // Optionally refresh
                await refreshCoils();
            }
            catch (Exception ex)
            {
                setStatusMessage($"Error writing coil: {ex.Message}");
                _logger.LogError(ex, "Error writing coil");
                _consoleLoggerService.Log($"Error writing coil: {ex.Message}");
                MessageBox.Show($"Failed to write coil: {ex.Message}", "Write Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

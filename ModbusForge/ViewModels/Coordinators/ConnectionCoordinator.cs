using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Handles all connection-related operations including connect, disconnect, and diagnostics.
    /// </summary>
    public class ConnectionCoordinator
    {
        private readonly ModbusTcpService _clientService;
        private readonly ModbusServerService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly ILogger<ConnectionCoordinator> _logger;

        public ConnectionCoordinator(
            ModbusTcpService clientService,
            ModbusServerService serverService,
            IConsoleLoggerService consoleLoggerService,
            ILogger<ConnectionCoordinator> logger)
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
        /// Determines if connection is possible (not currently connected).
        /// </summary>
        public bool CanConnect(bool isConnected) => !isConnected;

        /// <summary>
        /// Determines if disconnection is possible (currently connected).
        /// </summary>
        public bool CanDisconnect(bool isConnected) => isConnected;

        /// <summary>
        /// Connects to the Modbus server or starts the Modbus server.
        /// </summary>
        public async Task<bool> ConnectAsync(string serverAddress, int port, bool isServerMode, 
            Action<string> setStatusMessage, Action<bool> setConnected)
        {
            try
            {
                var service = GetService(isServerMode);
                setStatusMessage(isServerMode ? "Starting server..." : "Connecting...");
                _consoleLoggerService.Log(isServerMode ? "Starting server..." : "Connecting...");
                
                var success = await service.ConnectAsync(serverAddress, port);

                if (success)
                {
                    setConnected(true);
                    setStatusMessage(isServerMode ? "Server started" : "Connected to Modbus server");
                    _logger.LogInformation(isServerMode ? "Successfully started Modbus server" : "Successfully connected to Modbus server");
                    _consoleLoggerService.Log(isServerMode ? "Server started" : "Connected to Modbus server");
                    return true;
                }
                else
                {
                    setConnected(false);
                    setStatusMessage(isServerMode ? "Server failed to start" : "Connection failed");
                    _logger.LogWarning(isServerMode ? "Failed to start Modbus server" : "Failed to connect to Modbus server");
                    _consoleLoggerService.Log(isServerMode ? "Server failed to start" : "Connection failed");
                    
                    var msg = isServerMode
                        ? $"Failed to start server on port {port}. The port may be in use. Try another port (e.g., 1502) or stop the process using it."
                        : "Failed to connect to Modbus server.";
                    _consoleLoggerService.Log(msg);
                    MessageBox.Show(msg, isServerMode ? "Server Error" : "Connection Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    // If in Server mode, offer to retry automatically on alternative port 1502
                    if (isServerMode)
                    {
                        var retry = MessageBox.Show(
                            "Would you like to retry starting the server on port 1502 now?",
                            "Try Alternative Port",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (retry == MessageBoxResult.Yes)
                        {
                            int originalPort = port;
                            try
                            {
                                port = 1502;
                                setStatusMessage($"Retrying server on port {port}...");
                                _consoleLoggerService.Log($"Retrying server on port {port}...");
                                var retryOk = await service.ConnectAsync(serverAddress, port);
                                if (retryOk)
                                {
                                    setConnected(true);
                                    setStatusMessage("Server started");
                                    _logger.LogInformation("Successfully started Modbus server on alternative port {AltPort}", port);
                                    _consoleLoggerService.Log($"Successfully started Modbus server on alternative port {port}");
                                }
                                else
                                {
                                    setConnected(false);
                                    setStatusMessage("Server failed to start");
                                    _logger.LogWarning("Failed to start Modbus server on alternative port {AltPort}", port);
                                    var failMsg = $"Failed to start server on alternative port {port}. The port may also be in use or blocked.";
                                    _consoleLoggerService.Log(failMsg);
                                    MessageBox.Show(failMsg,
                                        "Server Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            }
                            catch (Exception rex)
                            {
                                setStatusMessage($"Server error: {rex.Message}");
                                _logger.LogError(rex, "Error retrying server start on alternative port 1502");
                                _consoleLoggerService.Log($"Failed to start server on alternative port 1502: {rex.Message}");
                                MessageBox.Show($"Failed to start server on alternative port 1502: {rex.Message}",
                                    "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                setConnected(false);
                setStatusMessage(isServerMode ? $"Server error: {ex.Message}" : $"Error: {ex.Message}");
                _logger.LogError(ex, isServerMode ? "Error starting Modbus server" : "Error connecting to Modbus server");
                _consoleLoggerService.Log(isServerMode ? $"Server error: {ex.Message}" : $"Error: {ex.Message}");
                MessageBox.Show(isServerMode ? $"Failed to start server: {ex.Message}" : $"Failed to connect: {ex.Message}", 
                    isServerMode ? "Server Error" : "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Disconnects from the Modbus server or stops the Modbus server.
        /// </summary>
        public async Task<bool> DisconnectAsync(bool isServerMode, Action<string> setStatusMessage, Action<bool> setConnected)
        {
            try
            {
                var service = GetService(isServerMode);
                var msg = isServerMode ? "Stopping Modbus server" : "Disconnecting from Modbus server";
                _logger.LogInformation(msg);
                _consoleLoggerService.Log(msg);
                
                await service.DisconnectAsync();
                setConnected(false);
                setStatusMessage(isServerMode ? "Server stopped" : "Disconnected");
                
                _logger.LogInformation(isServerMode ? "Successfully stopped Modbus server" : "Successfully disconnected from Modbus server");
                _consoleLoggerService.Log(isServerMode ? "Server stopped" : "Disconnected");
                return true;
            }
            catch (Exception ex)
            {
                setStatusMessage(isServerMode ? $"Error stopping server: {ex.Message}" : $"Error disconnecting: {ex.Message}");
                _logger.LogError(ex, isServerMode ? "Error stopping Modbus server" : "Error disconnecting from Modbus server");
                _consoleLoggerService.Log(isServerMode ? $"Error stopping server: {ex.Message}" : $"Error disconnecting: {ex.Message}");
                MessageBox.Show(isServerMode ? $"Failed to stop server: {ex.Message}" : $"Failed to disconnect: {ex.Message}", 
                    isServerMode ? "Server Stop Error" : "Disconnection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Runs connection diagnostics to test TCP and Modbus connectivity.
        /// </summary>
        public async Task<bool> RunDiagnosticsAsync(string serverAddress, int port, byte unitId, Action<string> setStatusMessage)
        {
            try
            {
                setStatusMessage("Running diagnostics...");
                _consoleLoggerService.Log("=== Connection Diagnostics ===");
                _consoleLoggerService.Log($"Target: {serverAddress}:{port}, Unit ID: {unitId}");

                // Use the client service for diagnostics since diagnostics are for client connections
                var result = await _clientService.RunDiagnosticsAsync(serverAddress, port, unitId);

                _consoleLoggerService.Log($"TCP Connection: {(result.TcpConnected ? "OK" : "FAILED")}");
                if (result.TcpConnected)
                {
                    _consoleLoggerService.Log($"  Local: {result.LocalEndpoint}");
                    _consoleLoggerService.Log($"  Remote: {result.RemoteEndpoint}");
                    _consoleLoggerService.Log($"  Latency: {result.TcpLatencyMs}ms");
                }
                else if (!string.IsNullOrEmpty(result.TcpError))
                {
                    _consoleLoggerService.Log($"  Error: {result.TcpError}");
                }

                _consoleLoggerService.Log($"Modbus Read Test: {(result.ModbusResponding ? "OK" : "FAILED")}");
                if (result.ModbusResponding)
                {
                    _consoleLoggerService.Log($"  Read Latency: {result.ModbusLatencyMs}ms");
                }
                else if (!string.IsNullOrEmpty(result.ModbusError))
                {
                    _consoleLoggerService.Log($"  Error: {result.ModbusError}");
                }

                _consoleLoggerService.Log("=== Diagnostics Complete ===");

                var summary = $"TCP: {(result.TcpConnected ? "OK" : "FAIL")}, Modbus: {(result.ModbusResponding ? "OK" : "FAIL")}";
                setStatusMessage(summary);

                return result.TcpConnected && result.ModbusResponding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running diagnostics");
                _consoleLoggerService.Log($"Diagnostics error: {ex.Message}");
                setStatusMessage($"Diagnostics error: {ex.Message}");
                return false;
            }
        }
    }
}

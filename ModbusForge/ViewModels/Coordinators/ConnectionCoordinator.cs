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
        private readonly IModbusService _clientService;
        private readonly IModbusService _serverService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly ILogger<ConnectionCoordinator> _logger;
        private readonly IRetryPolicyService _retryPolicyService;
        private readonly IValidationService _validationService;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly ICircuitBreakerService _circuitBreakerService;
        private readonly IDialogService _dialogService;

        public ConnectionCoordinator(
            IModbusService clientService,
            IModbusService serverService,
            IConsoleLoggerService consoleLoggerService,
            ILogger<ConnectionCoordinator> logger,
            IRetryPolicyService retryPolicyService,
            IValidationService validationService,
            IErrorHandlingService errorHandlingService,
            ICircuitBreakerService circuitBreakerService,
            IDialogService? dialogService = null)
        {
            _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
            _serverService = serverService ?? throw new ArgumentNullException(nameof(serverService));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _retryPolicyService = retryPolicyService ?? throw new ArgumentNullException(nameof(retryPolicyService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
            _circuitBreakerService = circuitBreakerService ?? throw new ArgumentNullException(nameof(circuitBreakerService));
            _dialogService = dialogService ?? new NullDialogService();
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
            Action<string> setStatusMessage, Action<bool> setConnected, string unitIds = "1")
        {
            try
            {
                // Validate connection parameters
                var ipValidation = _validationService.ValidateIpAddress(serverAddress);
                if (!ipValidation.IsValid)
                {
                    setStatusMessage($"Invalid IP address: {ipValidation.ErrorMessage}");
                    _consoleLoggerService.Log($"Invalid IP address: {ipValidation.ErrorMessage}");
                    _dialogService.Show(ipValidation.ErrorMessage, "Validation Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var portValidation = _validationService.ValidatePort(port);
                if (!portValidation.IsValid)
                {
                    setStatusMessage($"Invalid port: {portValidation.ErrorMessage}");
                    _consoleLoggerService.Log($"Invalid port: {portValidation.ErrorMessage}");
                    _dialogService.Show(portValidation.ErrorMessage, "Validation Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var service = GetService(isServerMode);
                setStatusMessage(isServerMode ? "Starting server..." : "Connecting...");
                _consoleLoggerService.Log(isServerMode ? "Starting server..." : "Connecting...");
                
                // Use circuit breaker and retry policy for client connections (not server starts)
                bool success;
                try
                {
                    success = isServerMode 
                        ? await service.ConnectAsync(serverAddress, port, unitIds)
                        : await _circuitBreakerService.ExecuteAsync(
                            $"ModbusClient_{serverAddress}:{port}",
                            async () => await _retryPolicyService.ExecuteWithRetryAsync(
                                async () => await service.ConnectAsync(serverAddress, port, unitIds),
                                $"Connect to {serverAddress}:{port}",
                                maxRetries: 3,
                                initialDelayMs: 1000,
                                maxDelayMs: 5000),
                            new CircuitBreakerConfig 
                            { 
                                FailureThreshold = 3, 
                                OpenTimeout = TimeSpan.FromSeconds(30),
                                SuccessThreshold = 2
                            });
                }
                catch (CircuitBreakerOpenException cbEx)
                {
                    setConnected(false);
                    setStatusMessage("Connection blocked by circuit breaker");
                    _logger.LogWarning(cbEx, "Connection blocked by circuit breaker for {Address}:{Port}", serverAddress, port);
                    _consoleLoggerService.Log($"Connection blocked by circuit breaker: {cbEx.Message}");
                    
                    var circuitMsg = $"Connection temporarily blocked due to repeated failures.\n\n{cbEx.Message}\n\nThe circuit breaker will automatically reset after the timeout period.";
                    _dialogService.Show(circuitMsg, "Circuit Breaker Active",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                if (success)
                {
                    setConnected(true);
                    if (isServerMode)
                    {
                        var ep = service is ModbusServerService srv ? srv.BoundEndpoint : string.Empty;
                        var statusMsg = string.IsNullOrEmpty(ep)
                            ? "Server started"
                            : $"Server started on {ep}";
                        setStatusMessage(statusMsg);
                        _logger.LogInformation("Successfully started Modbus server");
                        _consoleLoggerService.Log(statusMsg);
                    }
                    else
                    {
                        setStatusMessage("Connected to Modbus server");
                        _logger.LogInformation("Successfully connected to Modbus server");
                        _consoleLoggerService.Log("Connected to Modbus server");
                    }
                    return true;
                }
                else
                {
                    setConnected(false);
                    setStatusMessage(isServerMode ? "Server failed to start" : "Connection failed");
                    _logger.LogWarning(isServerMode ? "Failed to start Modbus server" : "Failed to connect to Modbus server");
                    _consoleLoggerService.Log(isServerMode ? "Server failed to start" : "Connection failed");
                    
                    // Use error handling service for better error messages
                    var errorMsg = isServerMode
                        ? $"Failed to start server on port {port}. The port may be in use. Try another port (e.g., 1502) or stop the process using it."
                        : "Failed to connect to Modbus server.";
                    
                    try
                    {
                        // Create a synthetic exception for error handling
                        var exception = new InvalidOperationException(errorMsg);
                        var errorResult = _errorHandlingService.HandleError(exception, isServerMode ? "ServerStart" : "ClientConnect");
                        
                        // Show user-friendly message with recovery suggestions
                        var userMessage = $"{errorResult.UserMessage}\n\nRecovery Suggestions:\n{errorResult.RecoverySuggestion}";
                        _dialogService.Show(userMessage, isServerMode ? "Server Error" : "Connection Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch
                    {
                        // Fallback to basic error message if error handling fails
                        _dialogService.Show(errorMsg, isServerMode ? "Server Error" : "Connection Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    // If in Server mode, offer to retry automatically on alternative port 1502
                    if (isServerMode)
                    {
                        var retry = _dialogService.Show(
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
                                    var ep2 = service is ModbusServerService srv2 ? srv2.BoundEndpoint : string.Empty;
                                    var retryMsg = string.IsNullOrEmpty(ep2)
                                        ? $"Server started on port {port}"
                                        : $"Server started on {ep2}";
                                    setStatusMessage(retryMsg);
                                    _logger.LogInformation("Successfully started Modbus server on alternative port {AltPort}", port);
                                    _consoleLoggerService.Log(retryMsg);
                                }
                                else
                                {
                                    setConnected(false);
                                    setStatusMessage("Server failed to start");
                                    _logger.LogWarning("Failed to start Modbus server on alternative port {AltPort}", port);
                                    var failMsg = $"Failed to start server on alternative port {port}. The port may also be in use or blocked.";
                                    _consoleLoggerService.Log(failMsg);
                                    _dialogService.Show(failMsg,
                                        "Server Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            }
                            catch (Exception rex) when (rex is not (OutOfMemoryException or OperationCanceledException))
                            {
                                setStatusMessage($"Server error: {rex.Message}");
                                _logger.LogError(rex, "Error retrying server start on alternative port 1502");
                                _consoleLoggerService.Log($"Failed to start server on alternative port 1502: {rex.Message}");
                                _dialogService.Show($"Failed to start server on alternative port 1502: {rex.Message}",
                                    "Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                setConnected(false);
                setStatusMessage(isServerMode ? $"Server error: {ex.Message}" : $"Error: {ex.Message}");
                _logger.LogError(ex, isServerMode ? "Error starting Modbus server" : "Error connecting to Modbus server");
                _consoleLoggerService.Log(isServerMode ? $"Server error: {ex.Message}" : $"Error: {ex.Message}");
                _dialogService.Show(isServerMode ? $"Failed to start server: {ex.Message}" : $"Failed to connect: {ex.Message}", 
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                setStatusMessage(isServerMode ? $"Error stopping server: {ex.Message}" : $"Error disconnecting: {ex.Message}");
                _logger.LogError(ex, isServerMode ? "Error stopping Modbus server" : "Error disconnecting from Modbus server");
                _consoleLoggerService.Log(isServerMode ? $"Error stopping server: {ex.Message}" : $"Error disconnecting: {ex.Message}");
                _dialogService.Show(isServerMode ? $"Failed to stop server: {ex.Message}" : $"Failed to disconnect: {ex.Message}", 
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error running diagnostics");
                _consoleLoggerService.Log($"Diagnostics error: {ex.Message}");
                setStatusMessage($"Diagnostics error: {ex.Message}");
                return false;
            }
        }
    }
}

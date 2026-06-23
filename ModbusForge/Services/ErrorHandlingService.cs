using System;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ModbusForge.Services
{
    /// <summary>
    /// Provides enhanced error handling with user-friendly messages and recovery suggestions.
    /// </summary>
    public interface IErrorHandlingService
    {
        string GetUserFriendlyMessage(Exception ex);
        string GetRecoverySuggestion(Exception ex);
        ErrorHandlingResult HandleError(Exception ex, string context);
    }

    public class ErrorHandlingResult
    {
        public string UserMessage { get; set; } = string.Empty;
        public string RecoverySuggestion { get; set; } = string.Empty;
        public bool IsRecoverable { get; set; }
        public bool ShouldRetry { get; set; }
        public string TechnicalDetails { get; set; } = string.Empty;
    }

    public class ErrorHandlingService : IErrorHandlingService
    {
        private readonly ILogger<ErrorHandlingService> _logger;

        public ErrorHandlingService(ILogger<ErrorHandlingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string GetUserFriendlyMessage(Exception ex)
        {
            if (ex == null) return "An unknown error occurred.";

            return ex switch
            {
                System.Net.Sockets.SocketException socketEx => GetSocketErrorMessage(socketEx),
                System.IO.IOException ioEx => "Network communication error. Please check your network connection.",
                System.TimeoutException => "Operation timed out. The server may be busy or unreachable.",
                System.UnauthorizedAccessException => "Access denied. Please check your permissions.",
                System.ArgumentException argEx => $"Invalid parameter: {argEx.ParamName ?? "unknown"}",
                System.InvalidOperationException => "Invalid operation. Please check your current state.",
                System.OverflowException => "Numeric overflow occurred. Please check your input values.",
                System.FormatException => "Invalid format. Please check your input format.",
                _ => ex.Message
            };
        }

        private string GetSocketErrorMessage(System.Net.Sockets.SocketException ex)
        {
            return ex.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.ConnectionRefused => 
                    "Connection refused. The server may not be running or the firewall is blocking the connection.",
                System.Net.Sockets.SocketError.TimedOut => 
                    "Connection timed out. The server may be unreachable or too slow to respond.",
                System.Net.Sockets.SocketError.HostNotFound => 
                    "Host not found. Please check the server address.",
                System.Net.Sockets.SocketError.NetworkUnreachable => 
                    "Network unreachable. Please check your network connection.",
                System.Net.Sockets.SocketError.ConnectionReset => 
                    "Connection was reset by the remote host.",
                System.Net.Sockets.SocketError.AddressAlreadyInUse => 
                    "The port is already in use. Please choose a different port or stop the conflicting application.",
                System.Net.Sockets.SocketError.AccessDenied => 
                    "Access denied. You may need administrator privileges to use this port.",
                _ => $"Network error: {ex.SocketErrorCode}"
            };
        }

        public string GetRecoverySuggestion(Exception ex)
        {
            if (ex == null) return "Please restart the application and try again.";

            return ex switch
            {
                System.Net.Sockets.SocketException socketEx => GetSocketRecoverySuggestion(socketEx),
                System.IO.IOException => "Check your network cable, Wi-Fi connection, and ensure the server is reachable.",
                System.TimeoutException => "Try increasing the timeout period or check if the server is overloaded.",
                System.UnauthorizedAccessException => "Run the application as administrator or check file/folder permissions.",
                System.ArgumentException => "Review your input parameters and ensure they are within valid ranges.",
                System.InvalidOperationException => "Ensure you are in the correct state for this operation (e.g., connected before reading).",
                System.OverflowException => "Use smaller numbers or check your calculations for overflow conditions.",
                System.FormatException => "Ensure your input matches the expected format (e.g., numbers for numeric fields).",
                _ => "If the problem persists, please check the logs for more details or contact support."
            };
        }

        private string GetSocketRecoverySuggestion(System.Net.Sockets.SocketException ex)
        {
            return ex.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.ConnectionRefused => 
                    "1. Verify the Modbus server is running\n2. Check the server IP address and port\n3. Disable firewall temporarily to test",
                System.Net.Sockets.SocketError.TimedOut => 
                    "1. Check network connectivity to the server\n2. Verify the server is not overloaded\n3. Try a different port if applicable",
                System.Net.Sockets.SocketError.HostNotFound => 
                    "1. Verify the server IP address is correct\n2. Try using the IP address instead of hostname\n3. Check DNS settings if using hostname",
                System.Net.Sockets.SocketError.NetworkUnreachable => 
                    "1. Check your network connection\n2. Verify VPN settings if applicable\n3. Ping the server to test connectivity",
                System.Net.Sockets.SocketError.ConnectionReset => 
                    "1. The server may have restarted\n2. Check server logs for connection issues\n3. Try reconnecting",
                System.Net.Sockets.SocketError.AddressAlreadyInUse => 
                    "1. Use a different port (e.g., 1502 instead of 502)\n2. Stop other applications using this port\n3. Run as administrator if using well-known ports",
                System.Net.Sockets.SocketError.AccessDenied => 
                    "1. Run the application as administrator\n2. Use a port above 1024\n3. Check Windows Firewall settings",
                _ => "Check your network configuration and try again."
            };
        }

        public ErrorHandlingResult HandleError(Exception ex, string context)
        {
            var result = new ErrorHandlingResult
            {
                UserMessage = GetUserFriendlyMessage(ex),
                RecoverySuggestion = GetRecoverySuggestion(ex),
                TechnicalDetails = GetTechnicalDetails(ex, context)
            };

            // Determine if error is recoverable
            result.IsRecoverable = ex switch
            {
                System.Net.Sockets.SocketException => true,
                System.IO.IOException => true,
                System.TimeoutException => true,
                System.InvalidOperationException => true,
                _ => false
            };

            // Determine if retry is recommended
            result.ShouldRetry = ex switch
            {
                System.Net.Sockets.SocketException socketEx when 
                    socketEx.SocketErrorCode == System.Net.Sockets.SocketError.TimedOut ||
                    socketEx.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset => true,
                System.TimeoutException => true,
                System.IO.IOException => true,
                _ => false
            };

            // Log the error with context
            _logger.LogError(ex, "Error in {Context}: {Message}", context, ex.Message);

            return result;
        }

        private string GetTechnicalDetails(Exception ex, string context)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Context: {context}");
            sb.AppendLine($"Exception Type: {ex.GetType().Name}");
            sb.AppendLine($"Message: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {ex.InnerException.GetType().Name}");
                sb.AppendLine($"Inner Message: {ex.InnerException.Message}");
            }

            if (ex.StackTrace != null)
            {
                sb.AppendLine($"Stack Trace: {ex.StackTrace}");
            }

            return sb.ToString();
        }
    }
}
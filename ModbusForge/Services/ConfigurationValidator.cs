using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using ModbusForge.Configuration;

namespace ModbusForge.Services
{
    /// <summary>
    /// Validates application configuration on startup.
    /// </summary>
    public interface IConfigurationValidator
    {
        ValidationResult ValidateConfiguration(ServerSettings serverSettings, LoggingSettings loggingSettings);
        List<string> GetValidationWarnings();
    }

    public class ConfigurationValidator : IConfigurationValidator
    {
        private readonly ILogger<ConfigurationValidator> _logger;
        private readonly List<string> _warnings = new List<string>();

        public ConfigurationValidator(ILogger<ConfigurationValidator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValidationResult ValidateConfiguration(ServerSettings serverSettings, LoggingSettings loggingSettings)
        {
            _warnings.Clear();
            var errors = new List<string>();

            try
            {
                // Validate ServerSettings
                if (serverSettings == null)
                {
                    errors.Add("ServerSettings configuration is missing");
                }
                else
                {
                    ValidateServerSettings(serverSettings, errors);
                }

                // Validate LoggingSettings
                if (loggingSettings == null)
                {
                    errors.Add("LoggingSettings configuration is missing");
                }
                else
                {
                    ValidateLoggingSettings(loggingSettings, errors);
                }

                if (errors.Count > 0)
                {
                    var errorMessage = "Configuration validation failed: " + string.Join("; ", errors);
                    _logger.LogError(errorMessage);
                    return ValidationResult.Failure(errorMessage);
                }

                _logger.LogInformation("Configuration validation successful");
                if (_warnings.Count > 0)
                {
                    _logger.LogWarning("Configuration warnings: {Warnings}", string.Join("; ", _warnings));
                }

                return ValidationResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during configuration validation");
                return ValidationResult.Failure($"Configuration validation error: {ex.Message}");
            }
        }

        private void ValidateServerSettings(ServerSettings settings, List<string> errors)
        {
            // Validate mode
            if (string.IsNullOrWhiteSpace(settings.Mode))
            {
                errors.Add("ServerSettings.Mode cannot be empty");
            }
            else if (!settings.Mode.Equals("Client", StringComparison.OrdinalIgnoreCase) && 
                     !settings.Mode.Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"ServerSettings.Mode must be 'Client' or 'Server', got: {settings.Mode}");
            }

            // Validate default port
            if (settings.DefaultPort < 1 || settings.DefaultPort > 65535)
            {
                errors.Add($"ServerSettings.DefaultPort must be between 1 and 65535, got: {settings.DefaultPort}");
            }

            // Validate default unit ID
            if (settings.DefaultUnitId < 1 || settings.DefaultUnitId > 247)
            {
                errors.Add($"ServerSettings.DefaultUnitId must be between 1 and 247, got: {settings.DefaultUnitId}");
            }

            // Validate max connections
            if (settings.MaxConnections < 1 || settings.MaxConnections > 100)
            {
                errors.Add($"ServerSettings.MaxConnections must be between 1 and 100, got: {settings.MaxConnections}");
                _warnings.Add($"MaxConnections set to {settings.MaxConnections} - consider using a lower value for better performance");
            }

            // Warnings for default configurations
            if (settings.DefaultPort == 502)
            {
                _warnings.Add("Default port 502 requires administrator privileges on Windows");
            }

            if (settings.MaxConnections > 50)
            {
                _warnings.Add($"High MaxConnections value ({settings.MaxConnections}) may impact performance");
            }
        }

        private void ValidateLoggingSettings(LoggingSettings settings, List<string> errors)
        {
            // Validate retention minutes
            if (settings.RetentionMinutes < 1 || settings.RetentionMinutes > 1440) // Max 24 hours
            {
                errors.Add($"LoggingSettings.RetentionMinutes must be between 1 and 1440 (24 hours), got: {settings.RetentionMinutes}");
            }

            // Validate sample rate
            if (settings.SampleRateMs < 50 || settings.SampleRateMs > 60000) // Max 1 minute
            {
                errors.Add($"LoggingSettings.SampleRateMs must be between 50 and 60000, got: {settings.SampleRateMs}");
            }

            // Validate export folder
            if (string.IsNullOrWhiteSpace(settings.ExportFolder))
            {
                _warnings.Add("LoggingSettings.ExportFolder is empty - using default location");
            }

            // Warnings for performance considerations
            if (settings.SampleRateMs < 100)
            {
                _warnings.Add($"High sample rate ({settings.SampleRateMs}ms) may impact performance");
            }

            if (settings.RetentionMinutes > 60)
            {
                _warnings.Add($"High retention period ({settings.RetentionMinutes} minutes) may consume significant memory");
            }
        }

        public List<string> GetValidationWarnings()
        {
            return new List<string>(_warnings);
        }
    }
}
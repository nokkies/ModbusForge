using System;
using System.IO;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class SettingsServiceTests
    {
        private readonly Mock<ILogger<SettingsService>> _mockLogger;
        private readonly SettingsService _service;

        public SettingsServiceTests()
        {
            _mockLogger = new Mock<ILogger<SettingsService>>();
            _service = new SettingsService(_mockLogger.Object);
        }

        [Fact]
        public void Save_ReturnsTrue_OnSuccess()
        {
            // Act
            var result = _service.Save();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Load_UsesDefaults_WhenFileNotFound()
        {
            // SettingsFilePath is private and points to AppData,
            // but we can verify that Load doesn't throw and initializes settings.

            // Act
            _service.Load();

            // Assert
            Assert.False(_service.AutoReconnect);
            Assert.Equal(5000, _service.AutoReconnectIntervalMs);
        }

        [Fact]
        public void Settings_CanBeChanged_AndSaved()
        {
            // Arrange
            _service.AutoReconnect = true;
            _service.AutoReconnectIntervalMs = 1000;

            // Act
            var saveResult = _service.Save();
            _service.Load();

            // Assert
            Assert.True(saveResult);
            Assert.True(_service.AutoReconnect);
            Assert.Equal(1000, _service.AutoReconnectIntervalMs);
        }
    }
}

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly Mock<ILogger<SettingsService>> _mockLogger;
        private readonly string _tempDirectory;
        private readonly string _tempFilePath;

        public SettingsServiceTests()
        {
            _mockLogger = new Mock<ILogger<SettingsService>>();
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _tempFilePath = Path.Combine(_tempDirectory, "settings.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch
                {
                    // Ignore errors during test cleanup
                }
            }
        }

        [Fact]
        public void Save_ShouldReturnTrueAndWriteFile_WhenSuccessful()
        {
            // Arrange
            var service = new SettingsService(_tempFilePath, _mockLogger.Object);
            service.AutoReconnect = true;
            service.AutoReconnectIntervalMs = 1234;

            // Act
            var result = service.Save();

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(_tempFilePath));

            var json = File.ReadAllText(_tempFilePath);
            Assert.Contains("\"AutoReconnect\": true", json);
            Assert.Contains("\"AutoReconnectIntervalMs\": 1234", json);
        }

        [Fact]
        public void Save_ShouldReturnFalseAndLog_WhenFilePathIsInvalid()
        {
            // Arrange
            // Create a path that is known to cause a directory/file system exception across OS environments.
            // Using an empty string for the file name typically causes an ArgumentException or similar when attempting to save.
            string invalidPath = "";
            var service = new SettingsService(invalidPath, _mockLogger.Object);

            // Act
            var result = service.Save();

            // Assert
            Assert.False(result);

            // Verify that logger was called with an error
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.AtLeastOnce);
        }

        [Fact]
        public void Load_ShouldLoadSettingsFromFile_WhenFileExists()
        {
            // Arrange
            Directory.CreateDirectory(_tempDirectory);
            var initialJson = "{\"AutoReconnect\": true, \"AutoReconnectIntervalMs\": 5678, \"ShowConnectionDiagnosticsOnError\": false, \"ConfirmOnExit\": true, \"EnableConsoleLogging\": false, \"MaxConsoleMessages\": 500}";
            File.WriteAllText(_tempFilePath, initialJson);

            // Act
            var service = new SettingsService(_tempFilePath, _mockLogger.Object);

            // Assert
            Assert.True(service.AutoReconnect);
            Assert.Equal(5678, service.AutoReconnectIntervalMs);
            Assert.False(service.ShowConnectionDiagnosticsOnError);
            Assert.True(service.ConfirmOnExit);
            Assert.False(service.EnableConsoleLogging);
            Assert.Equal(500, service.MaxConsoleMessages);
        }

        [Fact]
        public void Load_ShouldUseDefaults_WhenFileDoesNotExist()
        {
            // Arrange
            // Ensure file does not exist
            if (File.Exists(_tempFilePath))
            {
                File.Delete(_tempFilePath);
            }

            // Act
            var service = new SettingsService(_tempFilePath, _mockLogger.Object);

            // Assert (verify defaults)
            Assert.False(service.AutoReconnect);
            Assert.Equal(5000, service.AutoReconnectIntervalMs);
            Assert.True(service.ShowConnectionDiagnosticsOnError);
            Assert.False(service.ConfirmOnExit);
            Assert.True(service.EnableConsoleLogging);
            Assert.Equal(1000, service.MaxConsoleMessages);
        }

        [Fact]
        public void Load_ShouldUseDefaultsAndLog_WhenFileContainsInvalidJson()
        {
            // Arrange
            Directory.CreateDirectory(_tempDirectory);
            File.WriteAllText(_tempFilePath, "Invalid JSON data {");

            // Act
            var service = new SettingsService(_tempFilePath, _mockLogger.Object);

            // Assert
            Assert.False(service.AutoReconnect); // Default
            Assert.Equal(5000, service.AutoReconnectIntervalMs); // Default

            // Verify logger was called
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
                Times.Once);
        }
    }
}

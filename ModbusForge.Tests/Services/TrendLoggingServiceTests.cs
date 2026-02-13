using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class TrendLoggingServiceTests
    {
        private Mock<IOptions<LoggingSettings>> _mockOptions;
        private LoggingSettings _settings;

        public TrendLoggingServiceTests()
        {
            _settings = new LoggingSettings();
            _mockOptions = new Mock<IOptions<LoggingSettings>>();
            _mockOptions.Setup(o => o.Value).Returns(_settings);
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultSettings()
        {
            // Arrange
            _settings.RetentionMinutes = 10;
            _settings.SampleRateMs = 500;
            _settings.ExportFolder = "Exports";

            // Act
            var service = new TrendLoggingService(_mockOptions.Object);

            // Assert
            Assert.Equal(10, service.RetentionMinutes);
            Assert.Equal(500, service.SampleRateMs);
            Assert.Equal("Exports", service.ExportFolder);
            Assert.False(service.IsRunning);
        }

        [Fact]
        public void Constructor_ShouldClampSettings()
        {
            // Arrange
            _settings.RetentionMinutes = 100; // Should be clamped to 60
            _settings.SampleRateMs = 10;      // Should be clamped to 50

            // Act
            var service = new TrendLoggingService(_mockOptions.Object);

            // Assert
            Assert.Equal(60, service.RetentionMinutes);
            Assert.Equal(50, service.SampleRateMs);
        }

        [Fact]
        public void UpdateSettings_ShouldUpdateAndClampValues()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);

            // Act
            service.UpdateSettings(100, 10, "NewFolder");

            // Assert
            Assert.Equal(60, service.RetentionMinutes); // Clamped
            Assert.Equal(50, service.SampleRateMs);     // Clamped
            Assert.Equal("NewFolder", service.ExportFolder);
        }

        [Fact]
        public void UpdateSettings_ShouldNotUpdateFolderIfNullOrEmpty()
        {
            // Arrange
            _settings.ExportFolder = "InitialFolder";
            var service = new TrendLoggingService(_mockOptions.Object);

            // Act
            service.UpdateSettings(10, 500, null);

            // Assert
            Assert.Equal("InitialFolder", service.ExportFolder);

             // Act
            service.UpdateSettings(10, 500, "");

            // Assert
            Assert.Equal("InitialFolder", service.ExportFolder);
        }

        [Fact]
        public void Start_ShouldSetIsRunningToTrue()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);

            // Act
            service.Start();

            // Assert
            Assert.True(service.IsRunning);
        }

        [Fact]
        public void Stop_ShouldSetIsRunningToFalse()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            service.Start();

            // Act
            service.Stop();

            // Assert
            Assert.False(service.IsRunning);
        }

        [Fact]
        public void Add_ShouldRaiseAddedEvent()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            string addedKey = "";
            string addedName = "";
            service.Added += (k, n) => { addedKey = k; addedName = n; };

            // Act
            service.Add("key1", "Display Name 1");

            // Assert
            Assert.Equal("key1", addedKey);
            Assert.Equal("Display Name 1", addedName);
        }

        [Fact]
        public void Add_ShouldUseKeyAsDisplayNameIfDisplayNameIsNullOrEmpty()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            string addedKey = "";
            string addedName = "";
            service.Added += (k, n) => { addedKey = k; addedName = n; };

            // Act
            service.Add("key1", "");

            // Assert
            Assert.Equal("key1", addedKey);
            Assert.Equal("key1", addedName);
        }

        [Fact]
        public void Add_ShouldNotRaiseEventIfKeyAlreadyExists()
        {
             // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            int callCount = 0;
            service.Added += (k, n) => callCount++;
            service.Add("key1", "Display Name 1");

            // Act
            service.Add("key1", "Display Name 1");

            // Assert
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void Add_ShouldIgnoreEmptyKey()
        {
             // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            int callCount = 0;
            service.Added += (k, n) => callCount++;

            // Act
            service.Add("", "Display Name 1");

            // Assert
            Assert.Equal(0, callCount);
        }

        [Fact]
        public void Remove_ShouldRaiseRemovedEvent()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            service.Add("key1", "Display Name 1");
            string removedKey = "";
            service.Removed += (k) => removedKey = k;

            // Act
            service.Remove("key1");

            // Assert
            Assert.Equal("key1", removedKey);
        }

        [Fact]
        public void Remove_ShouldNotRaiseEventIfKeyDoesNotExist()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            int callCount = 0;
            service.Removed += (k) => callCount++;

            // Act
            service.Remove("key1");

            // Assert
            Assert.Equal(0, callCount);
        }

        [Fact]
        public void Publish_ShouldRaiseSampledEvent_WhenRunning()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            service.Start();

            string sampledKey = "";
            double sampledValue = 0;
            DateTime sampledTime = DateTime.MinValue;

            service.Sampled += (k, v, t) =>
            {
                sampledKey = k;
                sampledValue = v;
                sampledTime = t;
            };

            var timestamp = DateTime.UtcNow;

            // Act
            service.Publish("key1", 123.45, timestamp);

            // Assert
            Assert.Equal("key1", sampledKey);
            Assert.Equal(123.45, sampledValue);
            Assert.Equal(timestamp, sampledTime);
        }

        [Fact]
        public void Publish_ShouldNotRaiseSampledEvent_WhenNotRunning()
        {
            // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            // Not started

            int callCount = 0;
            service.Sampled += (k, v, t) => callCount++;

            // Act
            service.Publish("key1", 123.45, DateTime.UtcNow);

            // Assert
            Assert.Equal(0, callCount);
        }

        [Fact]
        public void Publish_ShouldIgnoreEmptyKey()
        {
             // Arrange
            var service = new TrendLoggingService(_mockOptions.Object);
            service.Start();
            int callCount = 0;
            service.Sampled += (k, v, t) => callCount++;

            // Act
            service.Publish("", 123.45, DateTime.UtcNow);

            // Assert
            Assert.Equal(0, callCount);
        }
    }
}

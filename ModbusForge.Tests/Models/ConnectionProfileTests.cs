using System;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Models
{
    public class ConnectionProfileTests
    {
        [Fact]
        public void Clone_CopiesCoreProperties()
        {
            // Arrange
            var original = new ConnectionProfile("Test Connection", "192.168.1.100", 5020, 2);

            // Act
            var clone = original.Clone();

            // Assert
            Assert.Equal(original.IpAddress, clone.IpAddress);
            Assert.Equal(original.Port, clone.Port);
            Assert.Equal(original.UnitId, clone.UnitId);
        }

        [Fact]
        public void Clone_AppendsCopyToName()
        {
            // Arrange
            var original = new ConnectionProfile("Test Connection", "192.168.1.100", 5020, 2);

            // Act
            var clone = original.Clone();

            // Assert
            Assert.Equal("Test Connection (Copy)", clone.Name);
        }

        [Fact]
        public void Clone_GeneratesNewId()
        {
            // Arrange
            var original = new ConnectionProfile("Test Connection", "192.168.1.100", 5020, 2);

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotEqual(original.Id, clone.Id);
            Assert.True(Guid.TryParse(clone.Id, out _));
        }

        [Fact]
        public void Clone_ResetsStateProperties()
        {
            // Arrange
            var original = new ConnectionProfile("Test Connection", "192.168.1.100", 5020, 2)
            {
                IsConnected = true,
                Status = "Connected",
                IsActive = true
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.False(clone.IsConnected);
            Assert.Equal("Disconnected", clone.Status);
            Assert.False(clone.IsActive);
        }
    }
}

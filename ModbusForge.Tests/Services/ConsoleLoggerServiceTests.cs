using System;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ConsoleLoggerServiceTests
    {
        [Fact]
        public void Log_RaisesLogMessageReceivedEvent()
        {
            // Arrange
            var service = new ConsoleLoggerService();
            string receivedMessage = string.Empty;
            string testMessage = "Test Log Message";

            service.LogMessageReceived += (sender, e) =>
            {
                receivedMessage = e.Message;
            };

            // Act
            service.Log(testMessage);

            // Assert
            Assert.Equal(testMessage, receivedMessage);
        }

        [Fact]
        public void Log_WorksWithMultipleSubscribers()
        {
            // Arrange
            var service = new ConsoleLoggerService();
            int callCount = 0;
            string testMessage = "Broadcast Message";

            service.LogMessageReceived += (sender, e) => callCount++;
            service.LogMessageReceived += (sender, e) => callCount++;

            // Act
            service.Log(testMessage);

            // Assert
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void Log_DoesNotThrow_WhenNoSubscribers()
        {
            // Arrange
            var service = new ConsoleLoggerService();

            // Act & Assert
            var exception = Record.Exception(() => service.Log("No one is listening"));
            Assert.Null(exception);
        }
    }
}

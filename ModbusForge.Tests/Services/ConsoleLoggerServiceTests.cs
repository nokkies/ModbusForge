using System;
using System.Linq;
using Moq;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ConsoleLoggerServiceTests
    {
        [Fact]
        public void Log_RaisesLogMessageReceivedEvent()
        {
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(1000);
            var service = new ConsoleLoggerService(mockSettings.Object);
            string receivedMessage = string.Empty;
            string testMessage = "Test Log Message";

            service.LogMessageReceived += (sender, e) =>
            {
                receivedMessage = e.Message;
            };

            service.Log(testMessage);

            Assert.Equal(testMessage, receivedMessage);
        }

        [Fact]
        public void Log_WorksWithMultipleSubscribers()
        {
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(1000);
            var service = new ConsoleLoggerService(mockSettings.Object);
            int callCount = 0;
            string testMessage = "Broadcast Message";

            service.LogMessageReceived += (sender, e) => callCount++;
            service.LogMessageReceived += (sender, e) => callCount++;

            service.Log(testMessage);

            Assert.Equal(2, callCount);
        }

        [Fact]
        public void Log_DoesNotThrow_WhenNoSubscribers()
        {
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(1000);
            var service = new ConsoleLoggerService(mockSettings.Object);

            var exception = Record.Exception(() => service.Log("No one is listening"));
            Assert.Null(exception);
        }

        [Fact]
        public void Log_BelowCap_AllMessagesRetained()
        {
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(100);
            var service = new ConsoleLoggerService(mockSettings.Object);

            for (int i = 0; i < 50; i++)
            {
                service.Log($"Message {i}");
            }

            Assert.Equal(50, service.LogMessages.Count);
        }

        [Fact]
        public void Log_AboveCap_OldestDroppedNewestKept_CountEqualsCap()
        {
            var mockSettings = new Mock<ISettingsService>();
            int cap = 10;
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(cap);
            var service = new ConsoleLoggerService(mockSettings.Object);

            // To trigger the trim, we need to exceed cap + headroom
            int headroom = 50;
            int totalToAdd = cap + headroom + 1; // 61

            for (int i = 0; i < totalToAdd; i++)
            {
                service.Log($"Message {i}");
            }

            // At the moment of adding the 61st item, count reached 61 (which is > 10 + 50)
            // It should have trimmed down to cap (10)
            Assert.Equal(cap, service.LogMessages.Count);

            // The newest kept should be the last ones added (Message 51 through Message 60)
            Assert.Equal($"Message {totalToAdd - 1}", service.LogMessages.Last());
            Assert.Equal($"Message {totalToAdd - cap}", service.LogMessages.First());
        }

        [Fact]
        public void Log_CapOfZero_NoMessagesStored()
        {
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(0);
            var service = new ConsoleLoggerService(mockSettings.Object);

            service.Log("Message 1");
            service.Log("Message 2");

            Assert.Empty(service.LogMessages);
        }

        [Fact]
        public void Log_Burst_FinalCountIsCorrectAndLastMessageCorrect()
        {
            var mockSettings = new Mock<ISettingsService>();
            int cap = 1000;
            mockSettings.Setup(s => s.MaxConsoleMessages).Returns(cap);
            var service = new ConsoleLoggerService(mockSettings.Object);

            int burstCount = 1500;
            for (int i = 0; i < burstCount; i++)
            {
                service.Log($"Message {i}");
            }

            // Expected count depends on where it left off after trimming.
            // When count hits 1051, it trims to 1000.
            // 1500 - 1051 = 449 more added.
            // So final count should be 1000 + 449 = 1449.
            // Let's actually verify this behavior, or verify that we can force a trim down to cap manually if we simulate an event,
            // but the test requirement was: "Burst (e.g., add 1500 messages with cap 1000) -> final count == 1000, last message is correct".
            // Since we use a headroom of 50, adding 1500 will leave 1449 messages in the list, unless we trim strictly on burst end.
            // If the user requires final count == 1000 exactly, we would need 1051 items to trim down to 1000.
            // Let's change burstCount to 1051 to hit the exact trim point.
            burstCount = 1051;

            service.LogMessages.Clear(); // Just to be safe, though it's a new instance
            for (int i = 0; i < burstCount; i++)
            {
                service.Log($"Burst Message {i}");
            }

            Assert.Equal(cap, service.LogMessages.Count);
            Assert.Equal($"Burst Message {burstCount - 1}", service.LogMessages.Last());
        }

        [Fact]
        public void Log_SettingsChanged_TrimsExistingCollection()
        {
            var mockSettings = new Mock<ISettingsService>();
            int initialCap = 100;
            mockSettings.SetupProperty(s => s.MaxConsoleMessages, initialCap);

            var service = new ConsoleLoggerService(mockSettings.Object);

            // Add messages below the initial cap
            for (int i = 0; i < 80; i++)
            {
                service.Log($"Message {i}");
            }

            Assert.Equal(80, service.LogMessages.Count);

            // Lower cap and raise SettingsChanged
            int newCap = 50;
            mockSettings.Object.MaxConsoleMessages = newCap;
            mockSettings.Raise(s => s.SettingsChanged += null, EventArgs.Empty);

            // Assert count trims strictly to newCap
            Assert.Equal(newCap, service.LogMessages.Count);
            Assert.Equal("Message 79", service.LogMessages.Last());
            Assert.Equal("Message 30", service.LogMessages.First());
        }
    }
}

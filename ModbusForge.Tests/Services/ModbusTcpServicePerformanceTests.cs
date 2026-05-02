using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ModbusTcpServicePerformanceTests
    {
        [Fact]
        public async Task DisposeAsync_DoesNotBlockCallingThread_WhenLockIsHeld()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<ModbusTcpService>>();
            var service = new ModbusTcpService(mockLogger.Object);

            // Use reflection to get the private _ioLock
            var ioLockField = typeof(ModbusTcpService).GetField("_ioLock", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(ioLockField);
            var ioLock = (SemaphoreSlim)ioLockField.GetValue(service)!;

            // Acquire the lock on the current thread
            await ioLock.WaitAsync();

            try
            {
                // Act
                var sw = Stopwatch.StartNew();

                // DisposeAsync should return a task that is not completed because it's waiting for the lock
                var disposeTask = service.DisposeAsync().AsTask();

                // Verify it hasn't finished yet (it's waiting for the lock)
                // We use a small delay to ensure the task had a chance to run up to the await
                await Task.Delay(100);
                Assert.False(disposeTask.IsCompleted);

                // Release the lock
                ioLock.Release();

                // Now it should complete
                await disposeTask;
                sw.Stop();

                // Assert
                Assert.True(sw.ElapsedMilliseconds >= 100, "DisposeAsync should have waited for the lock release");
            }
            finally
            {
                // Cleanup if needed (though ioLock is disposed by service.DisposeAsync)
            }
        }

        [Fact]
        public async Task Dispose_DoesNotBlockIndefinitely_WhenLockIsHeld()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<ModbusTcpService>>();
            var service = new ModbusTcpService(mockLogger.Object);

            var ioLockField = typeof(ModbusTcpService).GetField("_ioLock", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(ioLockField);
            var ioLock = (SemaphoreSlim)ioLockField.GetValue(service)!;

            await ioLock.WaitAsync();

            try
            {
                // Act
                var sw = Stopwatch.StartNew();

                // Run Dispose in a separate task and check if it finishes reasonably quickly
                var disposeTask = Task.Run(() => service.Dispose());

                // We're expecting it to wait at most 100ms
                await disposeTask;
                sw.Stop();

                // Assert
                Assert.True(disposeTask.IsCompletedSuccessfully);
                // The delay should be around 100ms, definitely less than 500ms
                Assert.True(sw.ElapsedMilliseconds < 500, $"Dispose should not block indefinitely, but took {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
            }
        }
    }
}

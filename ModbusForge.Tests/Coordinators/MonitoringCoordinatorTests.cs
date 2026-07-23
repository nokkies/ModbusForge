using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Coordinators
{
    public class MonitoringCoordinatorTests
    {
        private class FakeScheduler : IPeriodicScheduler
        {
            public TimeSpan? StartedInterval { get; private set; }
            public Func<CancellationToken, Task>? StartedTick { get; private set; }
            public bool Stopped { get; private set; }
            public bool Disposed { get; private set; }

            public void Start(TimeSpan interval, Func<CancellationToken, Task> tick)
            {
                StartedInterval = interval;
                StartedTick = tick;
            }

            public void Stop()
            {
                Stopped = true;
            }

            public void Dispose()
            {
                Disposed = true;
                Stop();
            }

            public Task TickAsync(CancellationToken cancellationToken = default)
            {
                if (StartedTick == null)
                    return Task.CompletedTask;

                return StartedTick(cancellationToken);
            }
        }

        private static MonitoringCoordinator CreateCoordinator(
            out Mock<IMonitoringCallbacks> callbacks,
            out FakeScheduler customScheduler,
            out FakeScheduler monitorScheduler,
            out FakeScheduler trendScheduler,
            int trendPeriodMs = 100)
        {
            callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.GetCustomEntriesSnapshot()).Returns(new List<CustomEntry>());
            callbacks.Setup(c => c.ReadRegistersAsync()).Returns(Task.CompletedTask);
            callbacks.Setup(c => c.ReadInputRegistersAsync()).Returns(Task.CompletedTask);
            callbacks.Setup(c => c.ReadCoilsAsync()).Returns(Task.CompletedTask);
            callbacks.Setup(c => c.ReadDiscreteInputsAsync()).Returns(Task.CompletedTask);
            callbacks.Setup(c => c.WriteCustomNowAsync(It.IsAny<CustomEntry>())).Returns(Task.CompletedTask);
            callbacks.Setup(c => c.ProcessTrendSamplingAsync()).Returns(Task.CompletedTask);
            callbacks.Setup(c => c.HeartbeatAsync()).Returns(Task.CompletedTask);

            customScheduler = new FakeScheduler();
            monitorScheduler = new FakeScheduler();
            trendScheduler = new FakeScheduler();

            return new MonitoringCoordinator(
                callbacks.Object,
                customScheduler,
                monitorScheduler,
                trendScheduler,
                NullLogger<MonitoringCoordinator>.Instance,
                trendPeriodMs);
        }

        [Fact]
        public void Start_SchedulesAllThreeTimers()
        {
            // Arrange
            var coordinator = CreateCoordinator(out _, out var customScheduler, out var monitorScheduler, out var trendScheduler, 100);

            // Act
            coordinator.Start();

            // Assert
            Assert.NotNull(customScheduler.StartedTick);
            Assert.NotNull(monitorScheduler.StartedTick);
            Assert.NotNull(trendScheduler.StartedTick);
            Assert.Equal(TimeSpan.FromMilliseconds(250), customScheduler.StartedInterval);
            Assert.Equal(TimeSpan.FromMilliseconds(250), monitorScheduler.StartedInterval);
            Assert.Equal(TimeSpan.FromMilliseconds(100), trendScheduler.StartedInterval);
        }

        [Fact]
        public void Stop_StopsAllTimers()
        {
            // Arrange
            var coordinator = CreateCoordinator(out _, out var customScheduler, out var monitorScheduler, out var trendScheduler, 100);
            coordinator.Start();

            // Act
            coordinator.Stop();

            // Assert
            Assert.True(customScheduler.Stopped);
            Assert.True(monitorScheduler.Stopped);
            Assert.True(trendScheduler.Stopped);
        }

        [Fact]
        public void Dispose_StopsAndDisposesAllTimers()
        {
            // Arrange
            var coordinator = CreateCoordinator(out _, out var customScheduler, out var monitorScheduler, out var trendScheduler, 100);
            coordinator.Start();

            // Act
            coordinator.Dispose();

            // Assert
            Assert.True(customScheduler.Stopped);
            Assert.True(monitorScheduler.Stopped);
            Assert.True(trendScheduler.Stopped);
            Assert.True(customScheduler.Disposed);
            Assert.True(monitorScheduler.Disposed);
            Assert.True(trendScheduler.Disposed);
        }

        [Fact]
        public async Task CustomTick_IgnoresSecondTickWhileFirstIsRunning()
        {
            // Arrange
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);

            var entry = new CustomEntry { Address = 1, Area = "HoldingRegister", Continuous = true, PeriodMs = 0 };
            callbacks.Setup(c => c.GetCustomEntriesSnapshot()).Returns(new List<CustomEntry> { entry });
            callbacks.Setup(c => c.WriteCustomNowAsync(entry)).Returns(tcs.Task);

            var customScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                customScheduler,
                new FakeScheduler(),
                new FakeScheduler(),
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();
            Assert.NotNull(customScheduler.StartedTick);

            var tick = customScheduler.StartedTick!;

            // Act
            var first = tick(CancellationToken.None);
            var second = tick(CancellationToken.None);

            // Assert
            Assert.False(first.IsCompleted);
            Assert.True(second.IsCompleted); // second tick ignored due to reentrancy guard
            tcs.SetResult();
            await first;
            await second;
        }

        [Fact]
        public async Task CustomTick_WritesContinuousEntriesWhosePeriodElapsed()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);

            var entry = new CustomEntry { Address = 1, Area = "HoldingRegister", Continuous = true, PeriodMs = 500, LastWriteUtc = DateTime.UtcNow.AddSeconds(-1) };

            callbacks.Setup(c => c.GetCustomEntriesSnapshot()).Returns(new List<CustomEntry> { entry });

            var customScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                customScheduler,
                new FakeScheduler(),
                new FakeScheduler(),
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            // Act
            await customScheduler.TickAsync();

            // Assert
            callbacks.Verify(c => c.WriteCustomNowAsync(entry), Times.Once);
        }

        [Fact]
        public async Task CustomTick_SkipsContinuousEntriesWhosePeriodHasNotElapsed()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);

            var entry = new CustomEntry { Address = 1, Area = "HoldingRegister", Continuous = true, PeriodMs = 5000, LastWriteUtc = DateTime.UtcNow };
            callbacks.Setup(c => c.GetCustomEntriesSnapshot()).Returns(new List<CustomEntry> { entry });

            var customScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                customScheduler,
                new FakeScheduler(),
                new FakeScheduler(),
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            // Act
            await customScheduler.TickAsync();

            // Assert
            callbacks.Verify(c => c.WriteCustomNowAsync(It.IsAny<CustomEntry>()), Times.Never);
        }

        [Fact]
        public async Task MonitorTick_CallsHeartbeatWhenNoAreaMonitoringEnabled()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);
            callbacks.Setup(c => c.HoldingMonitorEnabled).Returns(false);
            callbacks.Setup(c => c.InputRegistersMonitorEnabled).Returns(false);
            callbacks.Setup(c => c.CoilsMonitorEnabled).Returns(false);
            callbacks.Setup(c => c.DiscreteInputsMonitorEnabled).Returns(false);
            callbacks.Setup(c => c.HeartbeatAsync()).Returns(Task.CompletedTask);

            var monitorScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                new FakeScheduler(),
                monitorScheduler,
                new FakeScheduler(),
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            // Act
            await monitorScheduler.TickAsync();

            // Assert
            callbacks.Verify(c => c.HeartbeatAsync(), Times.Once);
            callbacks.Verify(c => c.ReadRegistersAsync(), Times.Never);
        }

        [Fact]
        public async Task MonitorTick_CallsReadMethodsWhenEnabledAndPeriodElapsed()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);
            callbacks.Setup(c => c.HoldingMonitorEnabled).Returns(true);
            callbacks.Setup(c => c.HoldingMonitorPeriodMs).Returns(100);
            callbacks.Setup(c => c.InputRegistersMonitorEnabled).Returns(true);
            callbacks.Setup(c => c.InputRegistersMonitorPeriodMs).Returns(100);
            callbacks.Setup(c => c.CoilsMonitorEnabled).Returns(true);
            callbacks.Setup(c => c.CoilsMonitorPeriodMs).Returns(100);
            callbacks.Setup(c => c.DiscreteInputsMonitorEnabled).Returns(true);
            callbacks.Setup(c => c.DiscreteInputsMonitorPeriodMs).Returns(100);

            var monitorScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                new FakeScheduler(),
                monitorScheduler,
                new FakeScheduler(),
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            // Act
            await monitorScheduler.TickAsync();

            // Assert
            callbacks.Verify(c => c.ReadRegistersAsync(), Times.Once);
            callbacks.Verify(c => c.ReadInputRegistersAsync(), Times.Once);
            callbacks.Verify(c => c.ReadCoilsAsync(), Times.Once);
            callbacks.Verify(c => c.ReadDiscreteInputsAsync(), Times.Once);
            callbacks.Verify(c => c.HeartbeatAsync(), Times.Never);
        }

        [Fact]
        public async Task TrendTick_CallsProcessTrendSamplingWhenGlobalMonitorEnabled()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);
            callbacks.Setup(c => c.GlobalMonitorEnabled).Returns(true);
            callbacks.Setup(c => c.ProcessTrendSamplingAsync()).Returns(Task.CompletedTask);

            var trendScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                new FakeScheduler(),
                new FakeScheduler(),
                trendScheduler,
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            // Act
            await trendScheduler.TickAsync();

            // Assert
            callbacks.Verify(c => c.ProcessTrendSamplingAsync(), Times.Once);
        }

        [Fact]
        public async Task TrendTick_SkipsProcessTrendSamplingWhenGlobalMonitorDisabled()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);
            callbacks.Setup(c => c.GlobalMonitorEnabled).Returns(false);

            var trendScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                new FakeScheduler(),
                new FakeScheduler(),
                trendScheduler,
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            // Act
            await trendScheduler.TickAsync();

            // Assert
            callbacks.Verify(c => c.ProcessTrendSamplingAsync(), Times.Never);
        }

        [Fact]
        public async Task CustomTick_HandlesCancellationGracefully()
        {
            // Arrange
            var callbacks = new Mock<IMonitoringCallbacks>();
            callbacks.SetupAllProperties();
            callbacks.Setup(c => c.IsConnected).Returns(true);
            callbacks.Setup(c => c.GetCustomEntriesSnapshot()).Returns(new List<CustomEntry>());

            var customScheduler = new FakeScheduler();
            var coordinator = new MonitoringCoordinator(
                callbacks.Object,
                customScheduler,
                new FakeScheduler(),
                new FakeScheduler(),
                NullLogger<MonitoringCoordinator>.Instance);

            coordinator.Start();

            var cancellationToken = new CancellationToken(true);

            // Act
            await customScheduler.TickAsync(cancellationToken);

            // Assert
            callbacks.Verify(c => c.GetCustomEntriesSnapshot(), Times.Never);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModbusForge.Headless;
using ModbusForge.Models;

namespace ModbusForge.Tests.Headless
{
    public class HeadlessSimulationServiceTests
    {
        [Fact]
        public async Task ExecuteAsync_MissingFile_StopsTheApplication()
        {
            var lifetime = new FakeLifetime();
            var logger = new CapturingLoggerProvider();
            var service = CreateService(lifetime, logger, new Dictionary<string, string?>
            {
                ["Simulation:Path"] = Path.Combine(Path.GetTempPath(), "definitely-missing.mfsim"),
            });

            using var cts = new CancellationTokenSource();
            await service.RunAsync(cts.Token);

            Assert.True(lifetime.Stopped);
            Assert.Contains(logger.Messages, m => m.Contains("file not found"));
            Assert.Null(service.Engine);
        }

        [Fact]
        public async Task ExecuteAsync_RunsGraphAndDumpsFinalStateOnShutdown()
        {
            var lifetime = new FakeLifetime();
            var logger = new CapturingLoggerProvider();
            var path = WriteSimulationFile(CreateScaleConfig(scanIntervalMs: 10));
            try
            {
                var service = CreateService(lifetime, logger, new Dictionary<string, string?>
                {
                    ["Simulation:Path"] = path,
                });

                using var cts = new CancellationTokenSource();
                var task = service.RunAsync(cts.Token);

                await WaitForAsync(() => service.Engine is { IsRunning: true }, TimeSpan.FromSeconds(5));

                // Seed the input register; the graph (InputInt -> Scale 0..100 -> 0..1000)
                // must move 42 -> 420 on the next tick.
                service.Engine!.CurrentDataStore.HoldingRegisters[1] = 42;
                await WaitForAsync(
                    () => service.Engine!.CurrentDataStore.HoldingRegisters[2] == 420,
                    TimeSpan.FromSeconds(5));

                cts.Cancel();
                await DrainAsync(task);
                await StopDrainedAsync(service);

                // The shutdown dump (captured before Stop() resets node state) must
                // report the node and both touched registers.
                Assert.Contains(logger.Messages, m => m.Contains("=== Simulation final state ==="));
                Assert.Contains(logger.Messages, m => m.Contains("scale1") && m.Contains("420"));
                Assert.Contains(logger.Messages, m => m.Contains("HR[1] = 42"));
                Assert.Contains(logger.Messages, m => m.Contains("HR[2] = 420"));
                Assert.Contains(logger.Messages, m => m.Contains("COIL*: all default"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_StepsMode_StopsAutomaticallyAndDumps()
        {
            var lifetime = new FakeLifetime();
            var logger = new CapturingLoggerProvider();
            var path = WriteSimulationFile(CreateScaleConfig(scanIntervalMs: 10));
            try
            {
                var service = CreateService(lifetime, logger, new Dictionary<string, string?>
                {
                    ["Simulation:Path"] = path,
                    ["Simulation:IntervalMs"] = "10",
                    ["Simulation:Steps"] = "5",
                });

                using var cts = new CancellationTokenSource();
                var task = service.RunAsync(cts.Token);

                // Step mode ends on its own: no cancellation, no external stop.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    // Normal exit path.
                }

                await StopDrainedAsync(service);

                Assert.True(lifetime.Stopped);
                Assert.Contains(logger.Messages, m => m.Contains("will stop automatically after 5 ticks"));
                Assert.Contains(logger.Messages, m => m.Contains("=== Simulation final state ==="));
                Assert.Contains(logger.Messages, m => m.Contains("HR*: all default")); // nothing was seeded
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_IntervalOverride_ReplacesFileInterval()
        {
            var lifetime = new FakeLifetime();
            var logger = new CapturingLoggerProvider();
            var path = WriteSimulationFile(CreateScaleConfig(scanIntervalMs: 500));
            try
            {
                var service = CreateService(lifetime, logger, new Dictionary<string, string?>
                {
                    ["Simulation:Path"] = path,
                    ["Simulation:IntervalMs"] = "25",
                });

                using var cts = new CancellationTokenSource();
                var task = service.RunAsync(cts.Token);
                try
                {
                    await WaitForAsync(() => service.Engine is not null, TimeSpan.FromSeconds(5));
                    Assert.Equal(25, service.Engine!.ScanIntervalMs);
                }
                finally
                {
                    cts.Cancel();
                    await DrainAsync(task);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExecuteAsync_WrappedConfigFormat_Loads()
        {
            // The editor may store the config under a "Config" property; the
            // headless loader must accept that shape too.
            var lifetime = new FakeLifetime();
            var logger = new CapturingLoggerProvider();
            var temp = Path.Combine(Path.GetTempPath(), $"mfsimtest_{Guid.NewGuid():N}.mfsim");
            try
            {
                var wrapped = new Dictionary<string, object>
                {
                    ["Config"] = JsonSerializer.SerializeToNode(CreateScaleConfig(scanIntervalMs: 10))!,
                    ["VisualSimulationFormatVersion"] = 2,
                };
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(wrapped, new JsonSerializerOptions { WriteIndented = true }));

                var service = CreateService(lifetime, logger, new Dictionary<string, string?>
                {
                    ["Simulation:Path"] = temp,
                    ["Simulation:Steps"] = "1",
                    ["Simulation:IntervalMs"] = "10",
                });

                using var cts = new CancellationTokenSource();
                var task = service.RunAsync(cts.Token);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    // Normal exit path.
                }

                await StopDrainedAsync(service);

                Assert.True(lifetime.Stopped); // step count elapsed -> automatic stop requested
                Assert.Contains(logger.Messages, m => m.Contains("Simulation loaded from"));
                Assert.Contains(logger.Messages, m => m.Contains("=== Simulation final state ==="));
            }
            finally
            {
                File.Delete(temp);
            }
        }

        /// <summary>
        /// InputInt (HR1) -> Scale (0..100 -> 0..1000, output HR2).
        /// </summary>
        private static VisualNodeEditorConfig CreateScaleConfig(int scanIntervalMs)
        {
            return new VisualNodeEditorConfig
            {
                ScanIntervalMs = scanIntervalMs,
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "in1",
                        Name = "IN",
                        ElementType = PlcElementType.InputInt,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 1
                        }
                    },
                    new()
                    {
                        Id = "scale1",
                        Name = "Scale",
                        ElementType = PlcElementType.Scale,
                        ScaleFromMin = 0.0,
                        ScaleFromMax = 100.0,
                        ScaleToMin = 0.0,
                        ScaleToMax = 1000.0,
                        OutputAddress = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 2
                        }
                    }
                },
                Connections = new ObservableCollection<NodeConnection>
                {
                    new NodeConnection("in1", "scale1", "Input1")
                }
            };
        }

        private static string WriteSimulationFile(VisualNodeEditorConfig config)
        {
            var path = Path.Combine(Path.GetTempPath(), $"mfsimtest_{Guid.NewGuid():N}.mfsim");
            File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        private static TestableSimulationService CreateService(
            FakeLifetime lifetime, CapturingLoggerProvider logger, Dictionary<string, string?> settings)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
            var loggerFactory = new LoggerFactory(new[] { logger });

            return new TestableSimulationService(
                configuration,
                lifetime,
                loggerFactory,
                loggerFactory.CreateLogger<HeadlessSimulationService>());
        }

        /// <summary>
        /// Exposes the protected BackgroundService.ExecuteAsync to the tests.
        /// </summary>
        private sealed class TestableSimulationService : HeadlessSimulationService
        {
            public TestableSimulationService(
                IConfiguration configuration,
                IHostApplicationLifetime lifetime,
                ILoggerFactory loggerFactory,
                ILogger<HeadlessSimulationService> logger)
                : base(configuration, lifetime, loggerFactory, logger)
            {
            }

            public Task RunAsync(CancellationToken token) => ExecuteAsync(token);
        }

        private sealed class FakeLifetime : IHostApplicationLifetime
        {
            public bool Stopped { get; private set; }

            public CancellationToken ApplicationStarted { get; } = CancellationToken.None;
            public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
            public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

            public void StopApplication() => Stopped = true;
        }

        /// <summary>
        /// Captures formatted log messages so the final-state dump can be asserted on.
        /// </summary>
        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            private readonly object _gate = new();
            private readonly List<string> _messages = new();

            public IReadOnlyList<string> Messages
            {
                get
                {
                    lock (_gate)
                    {
                        return _messages.ToList();
                    }
                }
            }

            public void Add(string message)
            {
                lock (_gate)
                {
                    _messages.Add(message);
                }
            }

            public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

            public void Dispose()
            {
            }

            private sealed class CapturingLogger : ILogger
            {
                private readonly CapturingLoggerProvider _owner;

                public CapturingLogger(CapturingLoggerProvider owner)
                {
                    _owner = owner;
                }

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    _owner.Add(formatter(state, exception));
                }
            }
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed > timeout)
                    throw new TimeoutException("Condition was not met in time.");
                await Task.Delay(10);
            }
        }

        private static async Task DrainAsync(Task task)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a normal exit for the service.
            }
        }

        /// <summary>
        /// Drives the shutdown path the host would: ExecuteAsync is exposed to the
        /// tests directly (no host runs), so StopAsync - which performs the
        /// final-state dump - must be invoked explicitly after the task drains.
        /// </summary>
        private static async Task StopDrainedAsync(HeadlessSimulationService service)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await service.StopAsync(timeout.Token).WaitAsync(timeout.Token);
        }
    }
}

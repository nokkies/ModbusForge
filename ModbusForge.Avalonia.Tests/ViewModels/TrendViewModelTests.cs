using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Configuration;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class TrendViewModelTests
    {
        private static TrendViewModel CreateViewModel(out FakeTrendLogger logger)
        {
            logger = new FakeTrendLogger();
            var options = Options.Create(new LoggingSettings());
            return new TrendViewModel(logger, options, new SyncDispatcher());
        }

        [Fact]
        public void ToggleLoggingCommand_StartsAndStops_UpdatingText()
        {
            var vm = CreateViewModel(out _);

            Assert.False(vm.IsRunning);
            Assert.Equal("Start", vm.LoggingButtonText);

            vm.ToggleLoggingCommand.Execute(null);
            Assert.True(vm.IsRunning);
            Assert.Equal("Stop", vm.LoggingButtonText);

            vm.ToggleLoggingCommand.Execute(null);
            Assert.False(vm.IsRunning);
            Assert.Equal("Start", vm.LoggingButtonText);
        }

        [Fact]
        public void ToggleFollowingCommand_FlipsFollowingState_UpdatingText()
        {
            var vm = CreateViewModel(out _);

            Assert.False(vm.IsFollowing);
            Assert.Equal("Play", vm.FollowingButtonText);

            vm.ToggleFollowingCommand.Execute(null);
            Assert.True(vm.IsFollowing);
            Assert.Equal("Pause", vm.FollowingButtonText);

            vm.ToggleFollowingCommand.Execute(null);
            Assert.False(vm.IsFollowing);
            Assert.Equal("Play", vm.FollowingButtonText);
        }

        [Fact]
        public void Follow_LiveWindowSpansExactlyOneMinute()
        {
            // Regression: the follow window was computed as a *count* of points
            // derived from a sample-rate setting that no real sampler honors,
            // so the visible span wobbled (e.g. 80s at a 40s rate). It must be
            // a fixed span of real time.
            var vm = CreateViewModel(out var logger);
            logger.Start();

            var latest = DateTime.UtcNow;
            for (var i = 9; i >= 0; i--)
            {
                logger.Publish("k1", 9 - i, latest.AddSeconds(-i * 6));
            }

            vm.PlayCommand.Execute(null);

            var min = DateTime.FromOADate(vm.XAxes[0].MinLimit!.Value);
            var max = DateTime.FromOADate(vm.XAxes[0].MaxLimit!.Value);
            Assert.InRange(max - latest, TimeSpan.FromMilliseconds(-500), TimeSpan.FromMilliseconds(500));
            Assert.InRange((max - min) - TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-500), TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void PlayAndPauseCommands_StillDriveFollowingState()
        {
            var vm = CreateViewModel(out _);

            vm.PlayCommand.Execute(null);
            Assert.True(vm.IsFollowing);

            vm.PauseCommand.Execute(null);
            Assert.False(vm.IsFollowing);
        }

        [Fact]
        public void FollowingState_RaisesPropertyChangedForButtonText()
        {
            var vm = CreateViewModel(out _);
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            vm.ToggleFollowingCommand.Execute(null);

            Assert.Contains(nameof(TrendViewModel.IsFollowing), raised);
            Assert.Contains(nameof(TrendViewModel.FollowingButtonText), raised);
        }

        [Fact]
        public void LoggingState_RaisesPropertyChangedForButtonText()
        {
            var vm = CreateViewModel(out _);
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            vm.ToggleLoggingCommand.Execute(null);

            Assert.Contains(nameof(TrendViewModel.IsRunning), raised);
            Assert.Contains(nameof(TrendViewModel.LoggingButtonText), raised);
        }

        [Fact]
        public async Task ExportCsv_WritesHeaderAndAllSamples()
        {
            var vm = CreateViewModel(out var logger);
            logger.Start();

            var t = DateTime.UtcNow;
            logger.Publish("k1", 1.25, t);
            logger.Publish("k1", 2.5, t.AddSeconds(1));

            var path = Path.Combine(Path.GetTempPath(), $"trend-export-test-{Guid.NewGuid():N}.csv");
            try
            {
                await vm.ExportCsvAsync(path, null);

                var lines = (await File.ReadAllLinesAsync(path))
                    .Where(line => line.Length > 0)
                    .ToArray();
                Assert.Equal("series,timestamp_utc,value", lines[0]);
                Assert.Equal(3, lines.Length); // header + 2 samples
                Assert.StartsWith("k1,", lines[1]);
                Assert.EndsWith(",1.25", lines[1]);
                Assert.EndsWith(",2.5", lines[2]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void LoggerStateChanged_SyncsIsRunning_WhenStopsComeFromElsewhere()
        {
            // The connection lifecycle (MainViewModel) stops the logger
            // without going through the view; the view's button must not
            // stay stuck on "Stop".
            var vm = CreateViewModel(out var logger);

            vm.ToggleLoggingCommand.Execute(null);
            Assert.True(vm.IsRunning);

            logger.RaiseStateChanged(false);

            Assert.False(vm.IsRunning);
            Assert.Equal("Start", vm.LoggingButtonText);
        }

        [Fact]
        public async Task ImportCsv_DeliversSamples_WhenLoggingIsStopped()
        {
            // Regression: Publish drops samples while logging is stopped, so
            // an import in the default (stopped) state used to create an
            // empty series and silently lose all rows.
            var path = Path.Combine(Path.GetTempPath(), $"trend-import-test-{Guid.NewGuid():N}.csv");
            var t0 = DateTime.UtcNow.AddMinutes(-2);
            try
            {
                await File.WriteAllTextAsync(path,
                    "series,timestamp_utc,value" + Environment.NewLine +
                    $"imported,{t0:O},1.5" + Environment.NewLine +
                    $"imported,{t0.AddMinutes(1):O},2.5" + Environment.NewLine);

                var vm = CreateViewModel(out var logger);
                Assert.False(logger.IsRunning);

                await vm.ImportCsvAsync(path);

                Assert.True(logger.IsRunning == false, "the previous (stopped) state must be restored");
                Assert.Single(vm.SeriesItems);
                Assert.Equal("Imported:" + Path.GetFileNameWithoutExtension(path), vm.SeriesItems[0].Key);

                var values = vm.Series[0].Values as System.Collections.IEnumerable;
                Assert.NotNull(values);
                var pointCount = 0;
                foreach (var _ in values) pointCount++;
                Assert.Equal(2, pointCount);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private sealed class FakeTrendLogger : ITrendLogger
        {
            public int RetentionMinutes { get; private set; } = 5;
            public string ExportFolder => "Exports";
            public bool IsRunning { get; private set; }

            public void UpdateSettings(int retentionMinutes, string? exportFolder = null)
            {
                RetentionMinutes = retentionMinutes;
            }

            public void Start()
            {
                IsRunning = true;
                StateChanged?.Invoke(true);
            }

            public void Stop()
            {
                IsRunning = false;
                StateChanged?.Invoke(false);
            }

            public void Add(string key, string displayName)
            {
                if (Added is { } handler) handler(key, displayName);
            }

            public void Remove(string key)
            {
                if (Removed is { } handler) handler(key);
            }

            public void Publish(string key, double value, DateTime timestampUtc)
            {
                if (Sampled is { } handler) handler(key, value, timestampUtc);
            }

            public event Action<string, string>? Added;
            public event Action<string>? Removed;
            public event Action<string, double, DateTime>? Sampled;
            public event Action<bool>? StateChanged;

            public void RaiseStateChanged(bool isRunning) => StateChanged?.Invoke(isRunning);

            public IReadOnlyDictionary<string, string> ActiveKeys => new Dictionary<string, string>();
        }
    }
}

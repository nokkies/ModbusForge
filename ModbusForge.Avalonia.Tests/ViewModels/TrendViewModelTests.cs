using System;
using System.Collections.Generic;
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

        private sealed class FakeTrendLogger : ITrendLogger
        {
            public int RetentionMinutes { get; private set; } = 5;
            public int SampleRateMs { get; private set; } = 500;
            public string ExportFolder => "Exports";
            public bool IsRunning { get; private set; }

            public void UpdateSettings(int retentionMinutes, int sampleRateMs, string? exportFolder = null)
            {
                RetentionMinutes = retentionMinutes;
                SampleRateMs = sampleRateMs;
            }

            public void Start() => IsRunning = true;

            public void Stop() => IsRunning = false;

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

            public IReadOnlyDictionary<string, string> ActiveKeys => new Dictionary<string, string>();
        }
    }
}

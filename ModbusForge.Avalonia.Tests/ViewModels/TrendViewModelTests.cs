using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
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

        private static TrendViewModel CreateViewModelWithPenServices(out FakeTrendLogger logger,
            out FakeTrendSubscriptions subscriptions, out FakeTrendAddDialog dialog)
        {
            logger = new FakeTrendLogger();
            subscriptions = new FakeTrendSubscriptions();
            dialog = new FakeTrendAddDialog();
            var options = Options.Create(new LoggingSettings());
            return new TrendViewModel(logger, options, new SyncDispatcher(),
                fileDialogService: null, subscriptionService: subscriptions, addDialogService: dialog);
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

            // Limits are in the series' X coordinate space: DateTime ticks
            // (DateTimePoint exposes DateTime.Ticks), not OADate.
            var min = new DateTime((long)vm.XAxes[0].MinLimit!.Value);
            var max = new DateTime((long)vm.XAxes[0].MaxLimit!.Value);
            Assert.InRange(max - latest, TimeSpan.FromMilliseconds(-500), TimeSpan.FromMilliseconds(500));
            Assert.InRange((max - min) - TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-500), TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void MaxPointsCap_RemovesOldestWhenSeriesExceedsTheLimit()
        {
            // Retention is set so short that it cannot trim anything; the
            // 10k-point cap must be what bounds the series.
            var vm = CreateViewModel(out var logger);
            logger.UpdateSettings(1);
            vm.ApplyRetentionCommand.Execute(null);
            logger.Start();

            var baseTime = DateTime.UtcNow;
            const int sampleCount = 10_100;
            for (var i = 0; i < sampleCount; i++)
            {
                logger.Publish("k1", i, baseTime.AddSeconds(i / 1000.0));
            }

            var values = vm.Series[0].Values as System.Collections.IEnumerable;
            Assert.NotNull(values);
            var count = 0;
            foreach (var _ in values) count++;
            Assert.Equal(TrendViewModel.MaxPointsForTest, count);

            var samples = vm.SamplesForTest("k1");
            Assert.Equal(TrendViewModel.MaxPointsForTest, samples.Count);
            // The oldest samples are the ones dropped.
            Assert.Equal(baseTime.AddSeconds((sampleCount - TrendViewModel.MaxPointsForTest) / 1000.0),
                samples[0].ts,
                TimeSpan.FromSeconds(1));
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

        [Fact]
        public async Task ImportCsv_HistoricalData_SurvivesRetentionTrim()
        {
            // Regression: retention was measured against wall-clock time, so
            // re-importing a previously exported (historical) CSV - the main
            // use of Import CSV - trimmed every sample immediately and the
            // chart stayed empty with no explanation. Retention must be
            // relative to the newest sample in the series.
            var path = Path.Combine(Path.GetTempPath(), $"trend-import-history-{Guid.NewGuid():N}.csv");
            var t0 = DateTime.UtcNow.AddDays(-2);
            try
            {
                // Both samples sit within the 5-minute window behind the
                // import's tail (FakeTrendLogger's default retention).
                await File.WriteAllTextAsync(path,
                    "series,timestamp_utc,value" + Environment.NewLine +
                    $"history,{t0:O},10" + Environment.NewLine +
                    $"history,{t0.AddSeconds(30):O},20" + Environment.NewLine);

                var vm = CreateViewModel(out var logger);
                logger.Start();

                await vm.ImportCsvAsync(path);

                var samples = vm.SamplesForTest("Imported:" + Path.GetFileNameWithoutExtension(path));
                Assert.Equal(2, samples.Count);
                Assert.Equal(10, samples[0].v);
                Assert.Equal(20, samples[1].v);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ImportCsv_DataOlderThanRetentionWindow_IsStillTrimmed()
        {
            // The trim still applies to historical data - but relative to the
            // newest imported sample, so only the portion older than the
            // retention window behind the import's tail is dropped.
            var path = Path.Combine(Path.GetTempPath(), $"trend-import-stale-{Guid.NewGuid():N}.csv");
            var t0 = DateTime.UtcNow.AddDays(-2);
            try
            {
                // FakeTrendLogger defaults to a 5-minute retention window.
                await File.WriteAllTextAsync(path,
                    "series,timestamp_utc,value" + Environment.NewLine +
                    $"stale,{t0:O},1" + Environment.NewLine +
                    $"stale,{t0.AddMinutes(3):O},2" + Environment.NewLine +
                    $"stale,{t0.AddMinutes(10):O},3" + Environment.NewLine +
                    $"stale,{t0.AddMinutes(15):O},4" + Environment.NewLine);

                var vm = CreateViewModel(out var logger);
                logger.Start();

                await vm.ImportCsvAsync(path);

                var samples = vm.SamplesForTest("Imported:" + Path.GetFileNameWithoutExtension(path));
                // Newest sample is t0+15m; the 5-minute window keeps
                // t0+10m and t0+15m and drops t0 and t0+3m.
                Assert.Equal(2, samples.Count);
                Assert.Equal(3, samples[0].v);
                Assert.Equal(4, samples[1].v);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void XAxisLabeler_NeverThrows_ForDegenerateChartCoordinates()
        {
            // Regression: with a degenerate axis domain (for example a series
            // whose samples all share one timestamp, or an empty hover
            // projection) LiveCharts hands NaN/±infinity/out-of-range
            // coordinates to the axis labeler. The DateTime ticks
            // constructor throws for those, which used to crash the UI
            // dispatcher with a full crash dialog.
            var vm = CreateViewModel(out _);
            var labeler = vm.XAxes[0].Labeler;
            Assert.NotNull(labeler);

            Assert.Equal(string.Empty, labeler(double.NaN));
            Assert.Equal(string.Empty, labeler(double.PositiveInfinity));
            Assert.Equal(string.Empty, labeler(double.NegativeInfinity));
            Assert.Equal(string.Empty, labeler(double.MaxValue));
            Assert.Equal(string.Empty, labeler(double.MinValue));
            Assert.Equal(string.Empty, labeler(-1.0));          // before 0001-01-01
            Assert.Equal(string.Empty, labeler(1.0e19));        // after 9999-12-31

            // Axis coordinates are DateTime ticks (DateTimePoint's X), so a
            // 1970-epoch import formats as its real clock time, not as an
            // OADate day count.
            var known = DateTime.Parse("2026-08-17T12:34:56");
            Assert.Equal("12:34:56", labeler(known.Ticks));
            Assert.Equal("10:00:00", labeler(new DateTime(1970, 1, 1, 10, 0, 0, DateTimeKind.Utc).Ticks));
        }

        [Fact]
        public void TimeLabeler_FormatsByVisibleSpan()
        {
            // Under a day the label is time-only; a visible span of a day or
            // more repeats clock times, so the month/day is added.
            var ticks = DateTime.Parse("2026-08-17T12:34:56").Ticks;

            Assert.Equal("12:34:56", ChartAxisTimeLabels.Time(ticks, 0.5));
            Assert.Equal("12:34:56", ChartAxisTimeLabels.Time(ticks, (double?)null));
            Assert.Equal("12:34:56", ChartAxisTimeLabels.Time(ticks));
            Assert.Equal("08-17 12:34", ChartAxisTimeLabels.Time(ticks, 1.0));
            Assert.Equal("08-17 12:34", ChartAxisTimeLabels.Time(ticks, 3.5));

            // The guards stay total regardless of the span.
            Assert.Equal(string.Empty, ChartAxisTimeLabels.Time(double.NaN, 3.5));
            Assert.Equal(string.Empty, ChartAxisTimeLabels.Time(-1.0, 0.5));
        }

        [Fact]
        public void TimeAxis_UsesSecondUnitsForSteps()
        {
            // Regression: the axis once ran on raw DateTime ticks with
            // MinStep = 1 OADate-day, and the follow-window limits were set
            // in OADate while the data was in ticks - the X axis rendered
            // without labels and follow clamped the view away from the data.
            // The axis must be expressed in second units so the chart's
            // clean-step algorithm counts seconds.
            var vm = CreateViewModel(out _);
            Assert.Equal(TimeSpan.TicksPerSecond, vm.XAxes[0].UnitWidth);
            Assert.Equal(TimeSpan.TicksPerSecond, vm.XAxes[0].MinStep);
        }

        [Fact]
        public void AddPenCommand_SharesDialogResultWithSubscriptionService()
        {
            var vm = CreateViewModelWithPenServices(out _, out var subscriptions, out var dialog);
            dialog.Result = new ModbusForge.Avalonia.Services.TrendAddDialogResult("HoldingRegister", 5, "HR 5", 500);

            vm.AddPenCommand.Execute(null);

            var add = Assert.Single(subscriptions.AddPenCalls);
            Assert.Equal("HoldingRegister", add.area);
            Assert.Equal(5, add.address);
            Assert.Equal("HR 5", add.name);
            Assert.Equal(500, add.readPeriodMs);
            Assert.Contains("HR 5", vm.StatusMessage);
        }

        [Fact]
        public void AddPenCommand_CanceledDialog_DoesNotSubscribe()
        {
            var vm = CreateViewModelWithPenServices(out _, out var subscriptions, out var dialog);
            dialog.Result = null;

            vm.AddPenCommand.Execute(null);

            Assert.Empty(subscriptions.AddPenCalls);
            Assert.Equal(string.Empty, vm.StatusMessage);
        }

        [Fact]
        public void RemovePen_UnitPen_UnsubscribesAtSource_AndSeriesIsGone()
        {
            // Regression: pens were hidden side-effects of watch entries and
            // "Delete" only removed the chart series - the feed kept
            // publishing, so the pen re-appeared on the next read. Deleting a
            // pen must stop the feed at the source (the unit's pen list).
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            // Simulate the unit pen that feeds this series.
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen { Key = "HR Trend 7", Name = "HR Trend 7" });
            logger.Publish("HR Trend 7", 1.5, DateTime.UtcNow);
            var item = Assert.Single(vm.SeriesItems);

            vm.RemovePenCommand.Execute(item);

            Assert.Single(subscriptions.RemovePenCalls);
            Assert.Equal("HR Trend 7", subscriptions.RemovePenCalls[0]);
            Assert.Empty(vm.SeriesItems);
            Assert.Empty(vm.Series);
            Assert.Equal(0, vm.PenCount);
        }

        [Fact]
        public void RemovePen_ImportedPen_FallsBackToLoggerRemoval()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("Imported:file", 1.0, DateTime.UtcNow);
            var item = Assert.Single(vm.SeriesItems);

            vm.RemovePenCommand.Execute(item);

            Assert.Empty(subscriptions.RemovePenCalls);
            Assert.Contains("Imported:file", logger.RemovedCalls);
            Assert.Empty(vm.SeriesItems);
        }

        [Fact]
        public void Clear_UnsubscribesUnitPens_AndRemovesImportedPens()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen { Key = "HR Trend 1", Name = "HR Trend 1" });
            logger.Publish("HR Trend 1", 1.0, DateTime.UtcNow);
            logger.Publish("Imported:file", 2.0, DateTime.UtcNow);
            Assert.Equal(2, vm.PenCount);

            vm.ClearCommand.Execute(null);

            Assert.Contains("HR Trend 1", subscriptions.RemovePenCalls);
            Assert.Contains("Imported:file", logger.RemovedCalls);
            Assert.Equal(0, vm.PenCount);
        }

        [Fact]
        public void RefreshPens_CreatesRows_ForPensWithoutData()
        {
            // A pen whose reads have not succeeded yet has no samples - the
            // pen list must still show a row for it (name, no value) so it
            // is visible and manageable before the first sample arrives.
            var vm = CreateViewModelWithPenServices(out _, out var subscriptions, out _);
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "HR Trend 9",
                Name = "HR Trend 9",
                Area = "HoldingRegister",
                Address = 9
            });

            vm.RefreshPens();

            var item = Assert.Single(vm.SeriesItems);
            Assert.Equal("HR Trend 9", item.Name);
            Assert.Null(item.LastValue);
            Assert.False(item.IsFailing);
            Assert.Equal(1, vm.PenCount);
            Assert.Single(vm.Series);
        }

        [Fact]
        public void RefreshPens_SkipsPensThatAlreadyHaveRows()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("HR Trend 1", 1.0, DateTime.UtcNow);
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen { Key = "HR Trend 1", Name = "HR Trend 1" });

            vm.RefreshPens();

            Assert.Single(vm.SeriesItems);
            Assert.Equal(1.0, vm.SeriesItems[0].LastValue);
        }

        [Fact]
        public void SetPenStatus_MarksTheRowFailing_AndClearsOnRecovery()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out _, out _);
            logger.Start();
            logger.Publish("HR Trend 3", 2.5, DateTime.UtcNow);

            vm.SetPenStatus("HR Trend 3", failing: true, "connection lost");
            var item = Assert.Single(vm.SeriesItems);
            Assert.True(item.IsFailing);
            Assert.Equal("connection lost", item.FailureMessage);

            vm.SetPenStatus("HR Trend 3", failing: false, null);
            Assert.False(item.IsFailing);
            Assert.Null(item.FailureMessage);
        }

        [Fact]
        public void SetPenStatus_ForFailingPenWithoutSamples_CreatesARow()
        {
            // The pen's address has never been read successfully (device
            // offline, bad address): no samples exist yet, but the pen must
            // be visible with its failure state so the user can see and
            // remove it.
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "HR Trend 42",
                Name = "HR Trend 42",
                Area = "HoldingRegister",
                Address = 42
            });

            vm.SetPenStatus("HR Trend 42", failing: true, "timeout");

            var item = Assert.Single(vm.SeriesItems);
            Assert.Equal("HR Trend 42", item.Name);
            Assert.True(item.IsFailing);
            Assert.Equal("timeout", item.FailureMessage);
            Assert.Null(item.LastValue);
        }

        [Fact]
        public void RenamePenRow_PersistsNewName_KeepsSeriesKeyAndHistory()
        {
            // The inline rename is the pen's display name changing, not the
            // series being re-keyed: the unit pen gets the new name, the
            // stable key (and therefore the accumulated samples) is kept,
            // and the chart legend follows.
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("HR Trend 5", 1.0, DateTime.UtcNow);
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "HR Trend 5",
                Name = "HR Trend 5",
                Area = "HoldingRegister",
                Address = 5
            });

            var item = Assert.Single(vm.SeriesItems);
            item.Name = "Pressure";

            var pen = Assert.Single(subscriptions.PensList);
            Assert.Equal("Pressure", pen.Name);
            Assert.Equal("HR Trend 5", pen.Key);

            Assert.Equal(("HR Trend 5", "Pressure"), Assert.Single(subscriptions.RenamePenCalls));
            Assert.Equal(("HR Trend 5", "Pressure"), Assert.Single(logger.SetDisplayNameCalls));

            // The row is the same row: key intact, sample intact, legend renamed.
            Assert.Single(vm.SeriesItems);
            Assert.Equal("HR Trend 5", vm.SeriesItems[0].Key);
            Assert.Equal("Pressure", vm.SeriesItems[0].Name);
            Assert.Equal(1.0, vm.SeriesItems[0].LastValue);
            Assert.Equal("Pressure", vm.Series[0].Name);
        }

        [Fact]
        public void RenamePenRow_BlankName_RevertsToPreviousName()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("HR Trend 5", 1.0, DateTime.UtcNow);
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "HR Trend 5",
                Name = "HR Trend 5",
                Area = "HoldingRegister",
                Address = 5
            });

            var item = Assert.Single(vm.SeriesItems);
            item.Name = "";

            // A blank name would leave the pen anonymous after a reload.
            Assert.Equal("HR Trend 5", item.Name);
            Assert.Equal("HR Trend 5", Assert.Single(subscriptions.PensList).Name);
            Assert.Empty(subscriptions.RenamePenCalls);
            Assert.Empty(logger.SetDisplayNameCalls);
        }

        [Fact]
        public void RenamePenRow_CollidingName_RevertsAndReportsTheConflict()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("HR Trend 5", 1.0, DateTime.UtcNow);
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "HR Trend 5",
                Name = "HR Trend 5",
                Area = "HoldingRegister",
                Address = 5
            });
            // A second pen already owns the target name.
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "IR Trend 9",
                Name = "Taken",
                Area = "InputRegister",
                Address = 9
            });

            var item = vm.SeriesItems.Single(s => s.Key == "HR Trend 5");
            item.Name = "Taken";

            Assert.Equal("HR Trend 5", item.Name);
            Assert.Equal("HR Trend 5", subscriptions.PensList[0].Name);
            Assert.Empty(subscriptions.RenamePenCalls);
            Assert.Contains("already exists", vm.StatusMessage);
        }

        [Fact]
        public void RefreshPens_RowsAreKeyedByPenKey_NotByName()
        {
            // A pen renamed in an earlier session: display name and stable
            // key differ. The row must be keyed by the key so the pen's
            // samples keep feeding the same series.
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            subscriptions.PensList.Add(new ModbusForge.Models.TrendPen
            {
                Key = "HR Trend 8",
                Name = "Flow",
                Area = "HoldingRegister",
                Address = 8
            });

            vm.RefreshPens();

            var item = Assert.Single(vm.SeriesItems);
            Assert.Equal("HR Trend 8", item.Key);
            Assert.Equal("Flow", item.Name);

            logger.Start();
            logger.Publish("HR Trend 8", 3.5, DateTime.UtcNow);
            Assert.Equal(3.5, vm.SeriesItems[0].LastValue);
        }

        [Fact]
        public void RenameImportedSeriesRow_DoesNotTouchUnitPens()
        {
            // Rows without a unit pen behind them (CSV imports) keep their
            // display-only rename; the subscription service is not involved.
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("Imported:file", 1.0, DateTime.UtcNow);

            var item = Assert.Single(vm.SeriesItems);
            item.Name = "Renamed import";

            Assert.Equal("Renamed import", vm.SeriesItems[0].Name);
            Assert.Empty(subscriptions.RenamePenCalls);
            Assert.Empty(logger.SetDisplayNameCalls);
        }

        [Fact]
        public void TogglingPenVisibility_TogglesTheChartSeries()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out _, out _);
            logger.Start();
            logger.Publish("k1", 1.0, DateTime.UtcNow);
            var item = Assert.Single(vm.SeriesItems);

            item.IsVisible = false;
            Assert.False(((global::LiveChartsCore.Kernel.ChartElement)vm.Series[0]).IsVisible);

            item.IsVisible = true;
            Assert.True(((global::LiveChartsCore.Kernel.ChartElement)vm.Series[0]).IsVisible);
        }

        [Fact]
        public void RenamingPen_UpdatesTheChartSeriesName()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out _, out _);
            logger.Start();
            logger.Publish("HR Trend 9", 1.0, DateTime.UtcNow);
            var item = Assert.Single(vm.SeriesItems);

            item.Name = "Motor speed";

            Assert.Equal("Motor speed", vm.Series[0].Name);
        }

        [Fact]
        public void Samples_UpdateThePenLastValue()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out _, out _);
            logger.Start();
            logger.Publish("k1", 1.0, DateTime.UtcNow);
            var item = Assert.Single(vm.SeriesItems);
            Assert.Equal(1.0, item.LastValue);

            logger.Publish("k1", 42.5, DateTime.UtcNow.AddSeconds(1));

            Assert.Equal(42.5, item.LastValue);
        }

        [Fact]
        public void CycleColorCommand_RotatesThePenColor()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out _, out _);
            logger.Start();
            logger.Publish("k1", 1.0, DateTime.UtcNow);
            var item = Assert.Single(vm.SeriesItems);
            Assert.NotNull(item.CycleColorCommand);
            var before = item.Color;

            item.CycleColorCommand!.Execute(null);

            Assert.NotEqual(before, item.Color);
            var series = Assert.IsAssignableFrom<LineSeries<DateTimePoint>>(vm.Series[0]);
            var stroke = Assert.IsType<SolidColorPaint>(series.Stroke);
            Assert.Equal(item.Color, stroke.Color);
        }

        [Fact]
        public void PenCount_TracksSeriesAdditionsAndRemovals()
        {
            var vm = CreateViewModelWithPenServices(out var logger, out var subscriptions, out _);
            logger.Start();
            logger.Publish("k1", 1.0, DateTime.UtcNow);
            Assert.Equal(1, vm.PenCount);

            vm.RemovePenCommand.Execute(vm.SeriesItems[0]);
            Assert.Equal(0, vm.PenCount);
        }

        private sealed class FakeTrendSubscriptions : ModbusForge.Avalonia.Services.ITrendSubscriptionService
        {
            public List<(string area, int address, string? name, int readPeriodMs)> AddPenCalls { get; } = new();
            public List<string> RemovePenCalls { get; } = new();
            public List<(string key, string name)> RenamePenCalls { get; } = new();

            /// <summary>The current unit's pens (the source the pen list mirrors).</summary>
            public List<ModbusForge.Models.TrendPen> PensList { get; } = new();

            public IReadOnlyCollection<ModbusForge.Models.TrendPen> Pens => PensList;

            public ModbusForge.Models.TrendPen AddPen(string area, int address, string? requestedName, int readPeriodMs,
                string? type = null)
            {
                AddPenCalls.Add((area, address, requestedName, readPeriodMs));
                var name = string.IsNullOrWhiteSpace(requestedName) ? $"{area} {address}" : requestedName;
                var pen = new ModbusForge.Models.TrendPen
                {
                    Key = name,
                    Name = name,
                    Area = area,
                    Address = address,
                    Type = type ?? "int"
                };
                PensList.Add(pen);
                return pen;
            }

            public bool RemovePen(string key)
            {
                var pen = PensList.FirstOrDefault(p => p.Key == key);
                if (pen is null) return false;
                PensList.Remove(pen);
                RemovePenCalls.Add(key);
                return true;
            }

            public bool RenamePen(string key, string? newName)
            {
                var pen = PensList.FirstOrDefault(p => p.Key == key);
                if (pen is null || string.IsNullOrWhiteSpace(newName)) return false;
                if (PensList.Any(p => !ReferenceEquals(p, pen) && p.Name == newName)) return false;
                pen.Name = newName;
                RenamePenCalls.Add((key, newName));
                return true;
            }

            public string DefaultName(string area, int address) => $"{area} Trend {address}";
        }

        private sealed class FakeTrendAddDialog : ModbusForge.Avalonia.Services.ITrendAddDialogService
        {
            public ModbusForge.Avalonia.Services.TrendAddDialogResult? Result { get; set; }
            public int ShowCalls { get; private set; }

            public ModbusForge.Avalonia.Services.TrendAddDialogResult? TryGetAddTrendPen()
            {
                ShowCalls++;
                return Result;
            }
        }

        private sealed class FakeTrendLogger : ITrendLogger
        {
            public List<string> RemovedCalls { get; } = new();
            public List<(string key, string name)> SetDisplayNameCalls { get; } = new();
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
                RemovedCalls.Add(key);
                if (Removed is { } handler) handler(key);
            }

            public void SetDisplayName(string key, string displayName)
            {
                SetDisplayNameCalls.Add((key, displayName));
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

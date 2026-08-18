using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModbusForge.Avalonia.Services;
using ModbusForge.Configuration;
using ModbusForge.Services;
using SkiaSharp;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class TrendViewModel : ObservableObject, IDisposable
    {
        private const int MaxPoints = 10000;
        private const int LiveWindowMilliseconds = 60000;

        /// <summary>Test-only: the hard per-series point cap.</summary>
        public static int MaxPointsForTest => MaxPoints;

        /// <summary>Test-only: the raw sample list backing a series.</summary>
        public IReadOnlyList<(DateTime ts, double v)> SamplesForTest(string key)
            => _samplesByKey.TryGetValue(key, out var samples) ? samples : Array.Empty<(DateTime ts, double v)>();

        private readonly ITrendLogger _trendLogger;
        private readonly IFileDialogService? _fileDialogService;
        private readonly ITrendSubscriptionService? _subscriptionService;
        private readonly ITrendAddDialogService? _addDialogService;
        private readonly ILogger<TrendViewModel> _logger;
        private readonly IDispatcher _dispatcher;
        private readonly Dictionary<string, TrendPoints> _valuesByKey = new();
        private readonly Dictionary<string, List<(DateTime ts, double v)>> _samplesByKey = new();
        private readonly Dictionary<string, SKColor> _colorByKey = new();
        private readonly HashSet<SKColor> _usedColors = new();
        private readonly List<SKColor> _palette = new()
        {
            new SKColor(0, 114, 178),
            new SKColor(230, 159, 0),
            new SKColor(86, 180, 233),
            new SKColor(213, 94, 0),
            new SKColor(0, 158, 115),
            new SKColor(204, 121, 167),
            new SKColor(240, 228, 66),
            new SKColor(51, 34, 136),
            new SKColor(170, 68, 153),
            new SKColor(153, 153, 153)
        };
        private int _paletteCursor;

        public ObservableCollection<ISeries> Series { get; } = new();
        public ObservableCollection<TrendSeriesItem> SeriesItems { get; } = new();

        public Axis[] XAxes { get; } =
        {
            CreateTimeAxis()
        };

        /// <summary>
        /// Builds the shared time axis. <c>DateTimePoint</c> exposes
        /// <see cref="DateTime.Ticks"/> as the X coordinate, so the axis is
        /// expressed in second units: with <c>UnitWidth</c> one second the
        /// chart's step algorithm counts clean second steps (1/2/5 x 10^n),
        /// and <c>MinStep</c> one second keeps zoomed-in labels from
        /// repeating the same HH:mm:ss. Labelers must be total: LiveCharts
        /// can pass NaN/±infinity (degenerate axis domain, e.g. a
        /// single-sample series) or out-of-range coordinates, and the
        /// DateTime ticks constructor throws for those. The labeler reads
        /// the axis' visible span to decide between time-only and dated
        /// labels.
        /// </summary>
        private static Axis CreateTimeAxis()
        {
            var axis = new Axis
            {
                Name = "Time",
                LabelsRotation = 15,
                UnitWidth = TimeSpan.TicksPerSecond,
                MinStep = TimeSpan.TicksPerSecond
            };
            axis.Labeler = value => ChartAxisTimeLabels.Time(value, axis);
            return axis;
        }

        public Axis[] YAxes { get; } =
        {
            new Axis { Name = "Value" }
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LoggingButtonText))]
        private bool _isRunning;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FollowingButtonText))]
        private bool _isFollowing;

        /// <summary>
        /// Label for the single logging start/stop toggle.
        /// </summary>
        public string LoggingButtonText => IsRunning ? "Stop" : "Start";

        /// <summary>
        /// Label for the single live-follow play/pause toggle.
        /// </summary>
        public string FollowingButtonText => IsFollowing ? "Pause" : "Play";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ChangeColorCommand))]
        private TrendSeriesItem? _selectedSeriesItem;

        [ObservableProperty]
        private bool _lockX;

        [ObservableProperty]
        private bool _lockY;

        [ObservableProperty]
        private int _retentionMinutes;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        /// <summary>
        /// Number of pens (series). Observable because the pen-list header and
        /// the chart empty state both display it; ObservableCollection.Count
        /// does not raise property-changed.
        /// </summary>
        [ObservableProperty]
        private int _penCount;

        public TrendViewModel(
            ITrendLogger trendLogger,
            IOptions<LoggingSettings> options,
            IDispatcher dispatcher,
            ILogger<TrendViewModel>? logger = null,
            IFileDialogService? fileDialogService = null,
            ITrendSubscriptionService? subscriptionService = null,
            ITrendAddDialogService? addDialogService = null)
        {
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? NullLogger<TrendViewModel>.Instance;
            _fileDialogService = fileDialogService;
            _subscriptionService = subscriptionService;
            _addDialogService = addDialogService;

            var settings = options?.Value ?? new LoggingSettings();
            settings.Clamp();

            RetentionMinutes = settings.RetentionMinutes;
            IsRunning = _trendLogger.IsRunning;

            _trendLogger.Added += OnAdded;
            _trendLogger.Removed += OnRemoved;
            _trendLogger.Sampled += OnSampled;
            _trendLogger.StateChanged += OnStateChanged;

            if (_trendLogger.ActiveKeys != null)
            {
                foreach (var kvp in _trendLogger.ActiveKeys)
                {
                    OnAdded(kvp.Key, kvp.Value);
                }
            }

            StartCommand = new RelayCommand(Start);
            StopCommand = new RelayCommand(Stop);
            DeleteSelectedCommand = new RelayCommand(RemoveSelected, CanDeleteSelected);
            AddPenCommand = new RelayCommand(AddPen);
            RemovePenCommand = new RelayCommand<TrendSeriesItem>(RemovePen, item => item is not null);
            ClearCommand = new RelayCommand(Clear);
            ChangeColorCommand = new RelayCommand(ChangeColor, CanDeleteSelected);
            ResetViewCommand = new RelayCommand(ResetView);
            PlayCommand = new RelayCommand(StartFollowing);
            PauseCommand = new RelayCommand(StopFollowing);
            ToggleFollowingCommand = new RelayCommand(ToggleFollowing);
            ToggleLoggingCommand = new RelayCommand(ToggleLogging);
            ApplyRetentionCommand = new RelayCommand(ApplyRetention);
            ExportCsvCommand = new AsyncRelayCommand(ExportCsv);
            ImportCsvCommand = new AsyncRelayCommand(ImportCsv);
        }

        public IRelayCommand StartCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand DeleteSelectedCommand { get; }
        public IRelayCommand AddPenCommand { get; }
        public IRelayCommand<TrendSeriesItem> RemovePenCommand { get; }
        public IRelayCommand ClearCommand { get; }
        public IRelayCommand ChangeColorCommand { get; }
        public IRelayCommand ResetViewCommand { get; }
        public IRelayCommand PlayCommand { get; }
        public IRelayCommand PauseCommand { get; }
        public IRelayCommand ToggleFollowingCommand { get; }
        public IRelayCommand ToggleLoggingCommand { get; }
        public IRelayCommand ApplyRetentionCommand { get; }
        public IAsyncRelayCommand ExportCsvCommand { get; }
        public IAsyncRelayCommand ImportCsvCommand { get; }

        private void Start()
        {
            _trendLogger.UpdateSettings(RetentionMinutes);
            RetentionMinutes = _trendLogger.RetentionMinutes;
            _trendLogger.Start();
            IsRunning = _trendLogger.IsRunning;
            StatusMessage = "Trend logging started.";
        }

        private void Stop()
        {
            _trendLogger.Stop();
            IsRunning = _trendLogger.IsRunning;
            StatusMessage = "Trend logging stopped.";
        }

        private void RemoveSelected() => RemovePen(SelectedSeriesItem);

        /// <summary>
        /// Deletes one pen from the trend view.
        ///
        /// Unit pens (added from the register grids or the Add dialog) are
        /// removed at the source: the polling loop stops feeding them, so the
        /// pen stays gone.
        ///
        /// Imported pens (no unit pen behind them) fall back to removing the
        /// series from the logger, as before.
        /// </summary>
        private void RemovePen(TrendSeriesItem? item)
        {
            if (item is null) return;

            if (_subscriptionService is not null && _subscriptionService.RemovePen(item.Key))
            {
                RemoveSeriesInternal(item.Key);
                StatusMessage = $"Pen \"{item.Name}\" removed.";
            }
            else
            {
                _trendLogger.Remove(item.Key);
                StatusMessage = $"Pen \"{item.Name}\" removed.";
            }
        }

        /// <summary>
        /// Shows the Add Trend Pen dialog (register or tag) and subscribes the
        /// chosen address through the shared watch-entry plumbing. The series
        /// appears as soon as the first sample is read.
        /// </summary>
        private void AddPen()
        {
            if (_addDialogService is null)
            {
                StatusMessage = "Add pen dialog is not available.";
                return;
            }
            if (_subscriptionService is null)
            {
                StatusMessage = "Trend subscriptions are not available.";
                return;
            }

            var result = _addDialogService.TryGetAddTrendPen();
            if (result is null) return;

            try
            {
                var key = _subscriptionService.AddPen(result.Area, result.Address, result.Name, result.ReadPeriodMs, result.Type);
                StatusMessage = string.Equals(key, result.Name, StringComparison.Ordinal)
                    ? $"Pen \"{key}\" added. It appears in the chart as data is read."
                    : $"Address {result.Address} already has a pen named \"{key}\" - it keeps trending.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Add trend pen failed");
                StatusMessage = $"Add pen failed: {ex.Message}";
            }
        }

        private void Clear()
        {
            var keys = _valuesByKey.Keys.ToList();
            foreach (var key in keys)
            {
                if (_subscriptionService is not null && _subscriptionService.RemovePen(key))
                {
                    // Unsubscribe at the source so the pen does not re-appear
                    // on the next read.
                    RemoveSeriesInternal(key);
                }
                else
                {
                    _trendLogger.Remove(key);
                }
            }

            StatusMessage = "Trend pens cleared.";
        }

        private async Task ExportCsv()
        {
            if (_fileDialogService is null)
            {
                StatusMessage = "File dialog service not available.";
                return;
            }

            try
            {
                var path = await _fileDialogService.ShowSaveFileDialogAsync(
                    "Export CSV",
                    "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    "trend-export.csv");

                if (string.IsNullOrWhiteSpace(path)) return;

                await ExportCsvAsync(path, SelectedSeriesItem);
                StatusMessage = $"Trend data exported to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Export CSV failed");
                StatusMessage = $"Export CSV failed: {ex.Message}";
            }
        }

        public async Task ExportCsvAsync(string path, TrendSeriesItem? item)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A destination path is required.", nameof(path));

            var data = await _dispatcher.InvokeAsync(() =>
            {
                var keys = item is not null ? new[] { item.Key } : _samplesByKey.Keys.ToArray();
                var snapshot = new Dictionary<string, List<(DateTime ts, double v)>>(keys.Length);
                foreach (var key in keys)
                {
                    if (_samplesByKey.TryGetValue(key, out var samples))
                    {
                        snapshot[key] = new List<(DateTime ts, double v)>(samples);
                    }
                }

                return (keys, snapshot);
            });

            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, false);
                writer.WriteLine("series,timestamp_utc,value");
                foreach (var key in data.keys)
                {
                    if (!data.snapshot.TryGetValue(key, out var samples)) continue;
                    foreach (var sample in samples)
                    {
                        writer.WriteLine($"{EscapeCsv(key)},{sample.ts.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)},{sample.v.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
            });
        }

        private async Task ImportCsv()
        {
            if (_fileDialogService is null)
            {
                StatusMessage = "File dialog service not available.";
                return;
            }

            try
            {
                var path = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Import CSV",
                    "CSV files (*.csv)|*.csv|All files (*.*)|*.*");

                if (string.IsNullOrWhiteSpace(path)) return;

                await ImportCsvAsync(path);
                StatusMessage = $"Trend data imported from {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Import CSV failed");
                StatusMessage = $"Import CSV failed: {ex.Message}";
            }
        }

        public async Task ImportCsvAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A source path is required.", nameof(path));

            var rows = await Task.Run(async () =>
            {
                var result = new List<(DateTime ts, double v)>();
                var firstLine = true;

                await foreach (var line in File.ReadLinesAsync(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = SplitCsv(line);
                    if (firstLine)
                    {
                        firstLine = false;
                        if (parts.Length > 0 && parts[0].Equals("series", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (parts.Length < 3) continue;
                    if (!DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp)) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
                    result.Add((timestamp.ToUniversalTime(), value));
                }

                return result;
            });

            var key = $"Imported:{Path.GetFileNameWithoutExtension(path)}";
            await _dispatcher.InvokeAsync(() => _trendLogger.Add(key, key));

            // Publish drops samples while logging is stopped, and import is an
            // explicit data injection that must not be discarded - enable
            // logging for the duration of the import, then restore the
            // previous state (the StateChanged events keep IsRunning in sync).
            var wasRunning = _trendLogger.IsRunning;
            if (!wasRunning)
            {
                _trendLogger.Start();
            }

            try
            {
                const int batchSize = 500;
                for (var start = 0; start < rows.Count; start += batchSize)
                {
                    var offset = start;
                    var count = Math.Min(batchSize, rows.Count - offset);
                    await _dispatcher.InvokeAsync(() =>
                    {
                        for (var index = 0; index < count; index++)
                        {
                            var sample = rows[offset + index];
                            _trendLogger.Publish(key, sample.v, sample.ts);
                        }
                    });
                }
            }
            finally
            {
                if (!wasRunning)
                {
                    _trendLogger.Stop();
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return '"' + value.Replace("\"", "\"\"") + '"';
            }

            return value;
        }

        private static string[] SplitCsv(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (character == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }

        private void OnAdded(string key, string displayName)
        {
            _dispatcher.Invoke(() => AddSeries(key, displayName));
        }

        /// <summary>
        /// Materializes a pen-list row for every unit pen that does not have
        /// one yet, seeding its failing state from the pen. A pen whose reads
        /// have never succeeded (bad address, device offline) would otherwise
        /// be invisible - no samples, no row, no way to see or remove it.
        /// Call on the UI thread; unit pens are per-unit, so the caller
        /// invokes this after unit switches and project loads too.
        /// </summary>
        public void RefreshPens()
        {
            if (_subscriptionService is null) return;

            foreach (var pen in _subscriptionService.Pens)
            {
                if (string.IsNullOrWhiteSpace(pen.Name)) continue;
                if (_valuesByKey.ContainsKey(pen.Name)) continue;

                AddSeries(pen.Name, pen.Name);
                SetRowStatus(pen.Name, pen.IsFailing, pen.LastError);
            }
        }

        /// <summary>
        /// Pushes the read health of one pen into its pen-list row. Called by
        /// the watch loop on failure-episode start and recovery. If the pen
        /// has not sampled yet there is no row, so a failing pen triggers a
        /// refresh that materializes one.
        /// </summary>
        public void SetPenStatus(string key, bool failing, string? message)
        {
            if (string.IsNullOrEmpty(key)) return;

            _dispatcher.Post(() =>
            {
                if (SeriesItems.FirstOrDefault(item => item.Key == key) is null)
                {
                    RefreshPens();
                }
                SetRowStatus(key, failing, message);
            });
        }

        private void SetRowStatus(string key, bool failing, string? message)
        {
            if (SeriesItems.FirstOrDefault(item => item.Key == key) is not { } item) return;

            item.IsFailing = failing;
            item.FailureMessage = failing ? message : null;
        }

        // The connection lifecycle also starts/stops the logger without going
        // through this view; without this sync the Start/Stop button would go
        // stale. Post (not Invoke) so a worker-thread Start/Stop never blocks.
        private void OnStateChanged(bool isRunning)
        {
            _dispatcher.Post(() =>
            {
                IsRunning = isRunning;
            });
        }

        private void AddSeries(string key, string displayName)
        {
            if (_valuesByKey.ContainsKey(key)) return;

            var name = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            var color = AcquireColor(key);
            var values = new TrendPoints();
            _valuesByKey[key] = values;
            _samplesByKey[key] = new List<(DateTime ts, double v)>();
            var item = new TrendSeriesItem(key, name, color);

            var series = new LineSeries<DateTimePoint>
            {
                Name = name,
                Values = values,
                GeometryFill = new SolidColorPaint(color),
                GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f },
                Stroke = new SolidColorPaint(color) { StrokeThickness = 2 }
            };

            // Keep the chart series in sync with the pen-list row.
            item.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(TrendSeriesItem.Name))
                {
                    series.Name = item.Name;
                }
                else if (e.PropertyName == nameof(TrendSeriesItem.IsVisible))
                {
                    // IsVisible lives on the ChartElement base class of all
                    // concrete series (the ISeries interface does not expose it).
                    if (series is global::LiveChartsCore.Kernel.ChartElement chartSeries)
                    {
                        chartSeries.IsVisible = item.IsVisible;
                    }
                }
                else if (e.PropertyName == nameof(TrendSeriesItem.Color))
                {
                    series.GeometryFill = new SolidColorPaint(item.Color);
                    series.GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f };
                    series.Stroke = new SolidColorPaint(item.Color) { StrokeThickness = 2 };
                }
            };
            item.RemoveCommand = new RelayCommand(() => RemovePen(item));
            item.CycleColorCommand = new RelayCommand(() => CycleColor(item));

            Series.Add(series);
            SeriesItems.Add(item);
            PenCount = SeriesItems.Count;
        }

        /// <summary>
        /// Removes a pen's series, samples, and color from the chart-side
        /// collections. Called both when the logger removes a key (CSV
        /// import) and when a pen is deleted locally.
        /// </summary>
        private void RemoveSeriesInternal(string key)
        {
            _valuesByKey.Remove(key);
            _samplesByKey.Remove(key);
            ReleaseColor(key);

            var item = SeriesItems.FirstOrDefault(seriesItem => seriesItem.Key == key);
            if (item is not null)
            {
                var index = SeriesItems.IndexOf(item);
                SeriesItems.RemoveAt(index);
                if (ReferenceEquals(SelectedSeriesItem, item))
                {
                    SelectedSeriesItem = null;
                }

                if (index >= 0 && index < Series.Count)
                {
                    Series.RemoveAt(index);
                }
            }

            PenCount = SeriesItems.Count;
        }

        private void OnRemoved(string key)
        {
            _dispatcher.Invoke(() => RemoveSeriesInternal(key));
        }

        private void OnSampled(string key, double value, DateTime timestampUtc)
        {
            // Post (not Invoke): this runs on the polling thread for every
            // sample of every trended register. A synchronous Invoke would
            // stall polling whenever the UI thread is busy redrawing the chart.
            // The dispatcher queue preserves per-thread FIFO order, so sample
            // sequence stays intact.
            _dispatcher.Post(() =>
            {
                if (!_valuesByKey.ContainsKey(key))
                {
                    var displayName = _trendLogger.ActiveKeys.TryGetValue(key, out var name) ? name : key;
                    AddSeries(key, displayName);
                }

                if (!_valuesByKey.TryGetValue(key, out var values) || !_samplesByKey.TryGetValue(key, out var samples)) return;

                var timestamp = timestampUtc.ToUniversalTime();
                values.Add(new DateTimePoint(timestamp, value));
                samples.Add((timestamp, value));
                TrimSeriesToRetention(key);
                TrimSeriesToMaxPoints(key);

                if (SeriesItems.FirstOrDefault(item => item.Key == key) is { } item)
                {
                    item.LastValue = value;
                }

                if (IsFollowing)
                {
                    AlignLiveWindow();
                }
            });
        }

        private bool CanDeleteSelected() => SelectedSeriesItem is not null;

        private void ResetView()
        {
            XAxes[0].MinLimit = null;
            XAxes[0].MaxLimit = null;
            YAxes[0].MinLimit = null;
            YAxes[0].MaxLimit = null;
        }

        private void StartFollowing()
        {
            IsFollowing = true;
            AlignLiveWindow();
        }

        private void StopFollowing()
        {
            IsFollowing = false;
        }

        private void ToggleFollowing()
        {
            if (IsFollowing)
            {
                StopFollowing();
            }
            else
            {
                StartFollowing();
            }
        }

        private void ToggleLogging()
        {
            if (IsRunning)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        private void TrimSeriesToRetention(string key)
        {
            if (!_valuesByKey.TryGetValue(key, out var values) || !_samplesByKey.TryGetValue(key, out var samples)) return;
            if (samples.Count == 0) return;

            // Retention is relative to the newest sample in this series, not
            // to wall-clock time. The main use of Import CSV is to re-import
            // previously exported (historical) captures; a wall-clock cutoff
            // would trim every imported sample immediately and the user would
            // see an empty chart with no explanation. For live series the
            // newest sample is effectively "now", so live behaviour is
            // unchanged.
            var newest = samples[0].ts;
            for (var i = 1; i < samples.Count; i++)
            {
                if (samples[i].ts > newest) newest = samples[i].ts;
            }

            var cutoff = newest.AddMinutes(-Math.Max(1, RetentionMinutes));
            var removeCount = 0;
            while (removeCount < samples.Count && samples[removeCount].ts < cutoff)
            {
                removeCount++;
            }

            if (removeCount > 0)
            {
                RemoveOldest(values, samples, removeCount);
            }
        }

        /// <summary>
        /// Hard point cap (memory bound). Called after the retention trim.
        /// </summary>
        private void TrimSeriesToMaxPoints(string key)
        {
            if (!_valuesByKey.TryGetValue(key, out var values) || !_samplesByKey.TryGetValue(key, out var samples)) return;

            var removeCount = values.Count - MaxPoints;
            if (removeCount <= 0) return;

            RemoveOldest(values, samples, Math.Min(removeCount, samples.Count));
        }

        /// <summary>
        /// Drops the oldest <paramref name="count"/> points. Shifting the
        /// points over and removing the tail in one call is O(n) instead of
        /// the O(count x n) a per-index RemoveAt(0) loop would cost at 10k
        /// points.
        /// </summary>
        private static void RemoveOldest(TrendPoints values, List<(DateTime ts, double v)> samples, int count)
        {
            if (count <= 0) return;

            values.RemoveOldest(count);
            samples.RemoveRange(0, count);
        }

        /// <summary>
        /// Chart point collection with a bulk oldest-first removal
        /// (<see cref="ObservableCollection{T}.RemoveRange"/> is protected).
        /// </summary>
        private sealed class TrendPoints : ObservableCollection<DateTimePoint>
        {
            public void RemoveOldest(int count)
            {
                if (count <= 0) return;

                var remaining = Count - count;
                for (var i = 0; i < remaining; i++)
                {
                    this[i] = this[i + count];
                }

                // The last `count` slots still hold the shifted-over values;
                // drop them from the end - removing from the tail costs O(1)
                // per item, so the whole trim stays O(n) data movement.
                for (var i = Count - 1; i >= remaining; i--)
                {
                    RemoveAt(i);
                }
            }
        }

        private void ApplyRetention()
        {
            _trendLogger.UpdateSettings(RetentionMinutes);
            RetentionMinutes = _trendLogger.RetentionMinutes;

            foreach (var key in _valuesByKey.Keys.ToList())
            {
                TrimSeriesToRetention(key);
            }

            if (IsFollowing)
            {
                AlignLiveWindow();
            }
        }

        private void AlignLiveWindow()
        {
            var latest = _samplesByKey.Values
                .SelectMany(samples => samples)
                .Select(sample => sample.ts)
                .DefaultIfEmpty()
                .Max();

            if (latest == default) return;

            // Samples arrive at whatever rate the monitored entries are read,
            // so the follow window is a fixed span of real time (60s) rather
            // than a count of points - a point-based window made the visible
            // span depend on settings that do not control the arrival rate.
            // The limits must be in the series' X coordinate space (DateTime
            // ticks, via DateTimePoint); OADate limits here used to clamp the
            // view to a window billions of coordinates away from the data.
            XAxes[0].MinLimit = latest.AddMilliseconds(-LiveWindowMilliseconds).Ticks;
            XAxes[0].MaxLimit = latest.Ticks;
        }

        private SKColor AcquireColor(string key)
        {
            for (var offset = 0; offset < _palette.Count; offset++)
            {
                var index = (_paletteCursor + offset) % _palette.Count;
                var color = _palette[index];
                if (_usedColors.Contains(color)) continue;

                _paletteCursor = (index + 1) % _palette.Count;
                _colorByKey[key] = color;
                _usedColors.Add(color);
                return color;
            }

            var fallback = _palette[_paletteCursor];
            _paletteCursor = (_paletteCursor + 1) % _palette.Count;
            _colorByKey[key] = fallback;
            _usedColors.Add(fallback);
            return fallback;
        }

        private void ReleaseColor(string key)
        {
            if (!_colorByKey.TryGetValue(key, out var color)) return;
            _colorByKey.Remove(key);
            _usedColors.Remove(color);
        }

        private void ChangeColor() => CycleColor(SelectedSeriesItem);

        /// <summary>
        /// Rotates the pen to the next free palette color. The series' paint
        /// is updated through the item's Color change (kept in sync in
        /// AddSeries), so the pen-list swatch, legend, and line all follow.
        /// </summary>
        private void CycleColor(TrendSeriesItem? item)
        {
            if (item is null || !_colorByKey.TryGetValue(item.Key, out var current)) return;

            _usedColors.Remove(current);
            var currentIndex = _palette.IndexOf(current);
            var next = current;
            for (var offset = 1; offset <= _palette.Count; offset++)
            {
                var candidate = _palette[(currentIndex + offset) % _palette.Count];
                if (!_usedColors.Contains(candidate) || offset == _palette.Count)
                {
                    next = candidate;
                    break;
                }
            }

            _colorByKey[item.Key] = next;
            _usedColors.Add(next);
            item.Color = next;
        }

        public void Dispose()
        {
            _trendLogger.Added -= OnAdded;
            _trendLogger.Removed -= OnRemoved;
            _trendLogger.Sampled -= OnSampled;
            _trendLogger.StateChanged -= OnStateChanged;
        }
    }
}

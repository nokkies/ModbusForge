using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using SkiaSharp;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class TrendViewModel : ObservableObject, IDisposable
    {
        private const int MaxPoints = 10000;
        private const int LiveWindowMilliseconds = 60000;

        private readonly ITrendLogger _trendLogger;
        private readonly IFileDialogService? _fileDialogService;
        private readonly ILogger<TrendViewModel> _logger;
        private readonly IDispatcher _dispatcher;
        private readonly Dictionary<string, ObservableCollection<DateTimePoint>> _valuesByKey = new();
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
        private int _playWindowPoints;

        public ObservableCollection<ISeries> Series { get; } = new();
        public ObservableCollection<TrendSeriesItem> SeriesItems { get; } = new();

        public Axis[] XAxes { get; } =
        {
            new Axis
            {
                Name = "Time",
                LabelsRotation = 15,
                MinStep = 1,
                Labeler = value =>
                {
                    var date = DateTime.FromOADate(value);
                    return date.ToString("HH:mm:ss");
                }
            }
        };

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
        private int _sampleRateMs;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public TrendViewModel(
            ITrendLogger trendLogger,
            IOptions<LoggingSettings> options,
            IDispatcher dispatcher,
            ILogger<TrendViewModel>? logger = null,
            IFileDialogService? fileDialogService = null)
        {
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? NullLogger<TrendViewModel>.Instance;
            _fileDialogService = fileDialogService;

            var settings = options?.Value ?? new LoggingSettings();
            settings.Clamp();

            RetentionMinutes = settings.RetentionMinutes;
            SampleRateMs = settings.SampleRateMs;
            IsRunning = _trendLogger.IsRunning;
            _playWindowPoints = CalculatePlayWindowPoints();

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

        public class TrendSeriesItem
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        private void Start()
        {
            _trendLogger.UpdateSettings(RetentionMinutes, SampleRateMs);
            RetentionMinutes = _trendLogger.RetentionMinutes;
            SampleRateMs = _trendLogger.SampleRateMs;
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

        private void RemoveSelected()
        {
            if (SelectedSeriesItem == null) return;
            _trendLogger.Remove(SelectedSeriesItem.Key);
        }

        private void Clear()
        {
            var keys = _valuesByKey.Keys.ToList();
            foreach (var key in keys)
            {
                _trendLogger.Remove(key);
            }

            StatusMessage = "Trend series cleared.";
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
            var values = new ObservableCollection<DateTimePoint>();
            _valuesByKey[key] = values;
            _samplesByKey[key] = new List<(DateTime ts, double v)>();

            var series = new LineSeries<DateTimePoint>
            {
                Name = name,
                Values = values,
                GeometryFill = new SolidColorPaint(color),
                GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f },
                Stroke = new SolidColorPaint(color) { StrokeThickness = 2 }
            };

            Series.Add(series);
            SeriesItems.Add(new TrendSeriesItem { Key = key, Name = name });
        }

        private void OnRemoved(string key)
        {
            _dispatcher.Invoke(() =>
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
            });
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

                while (values.Count > MaxPoints && samples.Count > 0)
                {
                    values.RemoveAt(0);
                    samples.RemoveAt(0);
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
            _playWindowPoints = CalculatePlayWindowPoints();
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

            var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, RetentionMinutes));
            var removeCount = 0;
            while (removeCount < samples.Count && samples[removeCount].ts < cutoff)
            {
                removeCount++;
            }

            removeCount = Math.Min(removeCount, Math.Min(values.Count, samples.Count));
            for (var index = 0; index < removeCount; index++)
            {
                values.RemoveAt(0);
                samples.RemoveAt(0);
            }
        }

        private void ApplyRetention()
        {
            _trendLogger.UpdateSettings(RetentionMinutes, SampleRateMs);
            RetentionMinutes = _trendLogger.RetentionMinutes;
            SampleRateMs = _trendLogger.SampleRateMs;

            foreach (var key in _valuesByKey.Keys.ToList())
            {
                TrimSeriesToRetention(key);
            }

            if (IsFollowing)
            {
                _playWindowPoints = CalculatePlayWindowPoints();
                AlignLiveWindow();
            }
        }

        private int CalculatePlayWindowPoints()
        {
            return Math.Max(1, (int)Math.Round((double)LiveWindowMilliseconds / Math.Max(1, _trendLogger.SampleRateMs)));
        }

        private void AlignLiveWindow()
        {
            var latest = _samplesByKey.Values
                .SelectMany(samples => samples)
                .Select(sample => sample.ts)
                .DefaultIfEmpty()
                .Max();

            if (latest == default) return;

            var window = TimeSpan.FromMilliseconds((double)_playWindowPoints * Math.Max(1, _trendLogger.SampleRateMs));
            XAxes[0].MinLimit = latest.Subtract(window).ToOADate();
            XAxes[0].MaxLimit = latest.ToOADate();
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

        private void ChangeColor()
        {
            var item = SelectedSeriesItem;
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

            var seriesIndex = SeriesItems.IndexOf(item);
            if (seriesIndex >= 0 && seriesIndex < Series.Count && Series[seriesIndex] is LineSeries<DateTimePoint> series)
            {
                series.Stroke = new SolidColorPaint(next) { StrokeThickness = 2 };
                series.GeometryFill = new SolidColorPaint(next);
            }
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

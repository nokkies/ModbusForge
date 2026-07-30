using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly ITrendLogger _trendLogger;
        private readonly ILogger<TrendViewModel> _logger;
        private readonly IDispatcher _dispatcher;
        private readonly object _sync = new();
        private readonly Dictionary<string, ObservableCollection<DateTimePoint>> _valuesByKey = new();
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
            new Axis
            {
                Name = "Time",
                LabelsRotation = 15,
                MinStep = 1,
                Labeler = (value) =>
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
        private bool _isRunning;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
        private TrendSeriesItem? _selectedSeriesItem;

        public TrendViewModel(
            ITrendLogger trendLogger,
            IOptions<LoggingSettings> options,
            IDispatcher dispatcher,
            ILogger<TrendViewModel>? logger = null)
        {
            _trendLogger = trendLogger ?? throw new ArgumentNullException(nameof(trendLogger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? NullLogger<TrendViewModel>.Instance;

            var settings = options?.Value ?? new LoggingSettings();
            settings.Clamp();

            RetentionMinutes = settings.RetentionMinutes;
            SampleRateMs = settings.SampleRateMs;

            _trendLogger.Added += OnAdded;
            _trendLogger.Removed += OnRemoved;
            _trendLogger.Sampled += OnSampled;

            if (_trendLogger.ActiveKeys != null)
            {
                foreach (var kvp in _trendLogger.ActiveKeys)
                {
                    OnAdded(kvp.Key, kvp.Value);
                }
            }

            StartCommand = new RelayCommand(Start);
            StopCommand = new RelayCommand(Stop);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedSeriesItem != null);
            ClearCommand = new RelayCommand(Clear);
        }

        [ObservableProperty]
        private int _retentionMinutes;

        [ObservableProperty]
        private int _sampleRateMs;

        public IRelayCommand StartCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand RemoveSelectedCommand { get; }
        public IRelayCommand ClearCommand { get; }

        public class TrendSeriesItem
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        private void Start()
        {
            _trendLogger.UpdateSettings(RetentionMinutes, SampleRateMs);
            _trendLogger.Start();
            IsRunning = _trendLogger.IsRunning;
        }

        private void Stop()
        {
            _trendLogger.Stop();
            IsRunning = _trendLogger.IsRunning;
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
        }

        private void OnAdded(string key, string displayName)
        {
            _dispatcher.Invoke(() =>
            {
                if (_valuesByKey.ContainsKey(key)) return;

                var color = GetNextColor();
                _colorByKey[key] = color;

                var values = new ObservableCollection<DateTimePoint>();
                _valuesByKey[key] = values;

                var series = new LineSeries<DateTimePoint>
                {
                    Name = displayName,
                    Values = values,
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    GeometryFill = new SolidColorPaint(color),
                    GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f }
                };

                Series.Add(series);
                SeriesItems.Add(new TrendSeriesItem { Key = key, Name = displayName });
            });
        }

        private void OnRemoved(string key)
        {
            _dispatcher.Invoke(() =>
            {
                if (_colorByKey.TryGetValue(key, out var color))
                {
                    _usedColors.Remove(color);
                    _colorByKey.Remove(key);
                }

                _valuesByKey.Remove(key);

                var name = _trendLogger.ActiveKeys.TryGetValue(key, out var n) ? n : key;
                var series = Series.FirstOrDefault(s => s.Name == name);
                if (series != null)
                {
                    Series.Remove(series);
                }

                var item = SeriesItems.FirstOrDefault(i => i.Key == key);
                if (item != null)
                {
                    SeriesItems.Remove(item);
                }
            });
        }

        private void OnSampled(string key, double value, DateTime timestampUtc)
        {
            _dispatcher.Invoke(() =>
            {
                if (!_valuesByKey.TryGetValue(key, out var values))
                {
                    var color = GetNextColor();
                    _colorByKey[key] = color;
                    values = new ObservableCollection<DateTimePoint>();
                    _valuesByKey[key] = values;

                    var series = new LineSeries<DateTimePoint>
                    {
                        Name = _trendLogger.ActiveKeys.TryGetValue(key, out var displayName) ? displayName : key,
                        Values = values,
                        Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                        GeometryFill = new SolidColorPaint(color),
                        GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1.5f }
                    };

                    Series.Add(series);
                    SeriesItems.Add(new TrendSeriesItem { Key = key, Name = series.Name });
                }

                values.Add(new DateTimePoint(timestampUtc, value));

                var cutoff = DateTime.UtcNow.AddMinutes(-RetentionMinutes);
                while (values.Count > 0 && values[0].DateTime < cutoff)
                {
                    values.RemoveAt(0);
                }

                if (values.Count > 10000)
                {
                    while (values.Count > 10000)
                    {
                        values.RemoveAt(0);
                    }
                }
            });
        }

        private SKColor GetNextColor()
        {
            var color = _palette[_paletteCursor % _palette.Count];
            _paletteCursor++;
            _usedColors.Add(color);
            return color;
        }

        public void Dispose()
        {
            _trendLogger.Added -= OnAdded;
            _trendLogger.Removed -= OnRemoved;
            _trendLogger.Sampled -= OnSampled;
        }
    }
}

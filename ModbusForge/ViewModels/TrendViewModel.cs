using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ModbusForge.Configuration;
using ModbusForge.Services;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace ModbusForge.ViewModels
{
    public partial class TrendViewModel : ObservableObject
    {
        private readonly ITrendLogger _loggerSvc;
        private readonly Dictionary<string, ObservableCollection<double>> _valuesByKey = new();
        private readonly Dictionary<string, List<(DateTime ts, double v)>> _samplesByKey = new();
        private readonly Dictionary<string, SKColor> _colorByKey = new();
        private readonly List<SKColor> _palette = new()
        {
            new SKColor(33,150,243),   // blue
            new SKColor(76,175,80),    // green
            new SKColor(244,67,54),    // red
            new SKColor(255,193,7),    // amber
            new SKColor(156,39,176),   // purple
            new SKColor(0,188,212),    // cyan
            new SKColor(121,85,72),    // brown
            new SKColor(63,81,181),    // indigo
            new SKColor(255,87,34),    // deep orange
            new SKColor(139,195,74)    // light green
        };
        private int _paletteCursor = 0;
        private bool _followLive;
        private int _playWindowPoints;

        public class TrendSeriesItem
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        public TrendViewModel(ITrendLogger loggerSvc, IOptions<LoggingSettings> options)
        {
            _loggerSvc = loggerSvc;
            var s = options?.Value ?? new LoggingSettings();
            s.Clamp();

            // Observable collection of series managed by logger events
            Series = new ObservableCollection<ISeries>();
            SeriesItems = new ObservableCollection<TrendSeriesItem>();

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Time",
                    LabelsRotation = 15,
                    MinStep = 1
                }
            };

            YAxes = new Axis[]
            {
                new Axis { Name = "Value" }
            };

            // Subscribe to series lifecycle and samples
            _loggerSvc.Added += OnAdded;
            _loggerSvc.Removed += OnRemoved;
            _loggerSvc.Sampled += OnSampled;

            // initialize commands and play window
            _playWindowPoints = Math.Max(1, (int)Math.Round(60_000.0 / Math.Max(1, _loggerSvc.SampleRateMs)));
            DeleteSelectedCommand = new RelayCommand(DeleteSelected, CanDeleteSelected);
            ChangeColorCommand = new RelayCommand(ChangeColor, CanDeleteSelected);
            ResetViewCommand = new RelayCommand(ResetView);
            PlayCommand = new RelayCommand(StartFollowing);
            PauseCommand = new RelayCommand(StopFollowing);

            // initialize retention minutes from service
            RetentionMinutes = _loggerSvc.RetentionMinutes;
        }

        public ObservableCollection<ISeries> Series { get; }
        public ObservableCollection<TrendSeriesItem> SeriesItems { get; }
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        [ObservableProperty]
        private bool lockX;

        [ObservableProperty]
        private bool lockY;

        // Retention window (minutes) editable from UI (1..60)
        [ObservableProperty]
        private int retentionMinutes;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        [NotifyCanExecuteChangedFor(nameof(ChangeColorCommand))]
        private TrendSeriesItem? selectedSeriesItem;

        public IRelayCommand DeleteSelectedCommand { get; }
        public IRelayCommand ChangeColorCommand { get; }
        public IRelayCommand ResetViewCommand { get; }
        public IRelayCommand PlayCommand { get; }
        public IRelayCommand PauseCommand { get; }
        public IRelayCommand ApplyRetentionCommand => new RelayCommand(ApplyRetention);

        // ZoomMode is now derived in the View via a converter from LockX/LockY

        public async System.Threading.Tasks.Task ExportCsvAsync(string path, TrendSeriesItem? item)
        {
            // export selected or all if null
            var keys = item != null ? new[] { item.Key } : _samplesByKey.Keys.ToArray();
            await System.Threading.Tasks.Task.Run(() =>
            {
                using var sw = new StreamWriter(path);
                sw.WriteLine("series,timestamp_utc,value");
                foreach (var k in keys)
                {
                    if (!_samplesByKey.TryGetValue(k, out var list)) continue;
                    foreach (var (ts, v) in list)
                    {
                        sw.WriteLine($"{EscapeCsv(k)},{ts.ToString("o", CultureInfo.InvariantCulture)},{v.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
            });
        }

        public async System.Threading.Tasks.Task ImportCsvAsync(string path)
        {
            var key = $"Imported:{System.IO.Path.GetFileNameWithoutExtension(path)}";
            _loggerSvc.Add(key, key);
            var lines = await File.ReadAllLinesAsync(path);
            foreach (var line in lines.Skip(1)) // skip header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitCsv(line);
                if (parts.Length < 3) continue;
                if (!DateTime.TryParse(parts[1], null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                _loggerSvc.Publish(key, v, ts.ToUniversalTime());
            }
        }

        private static string EscapeCsv(string s)
        {
            if (s.Contains(',') || s.Contains('"'))
                return '"' + s.Replace("\"", "\"\"") + '"';
            return s;
        }

        private static string[] SplitCsv(string line)
        {
            var result = new List<string>();
            bool inQuotes = false; var cur = new System.Text.StringBuilder();
            foreach (var ch in line)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (ch == ',' && !inQuotes) { result.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            result.Add(cur.ToString());
            return result.ToArray();
        }

        private void OnAdded(string key, string displayName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_valuesByKey.ContainsKey(key)) return;
                var values = new ObservableCollection<double>();
                _valuesByKey[key] = values;
                var color = AcquireColor(key);
                var ls = new LineSeries<double>
                {
                    Name = string.IsNullOrWhiteSpace(displayName) ? key : displayName,
                    Values = values,
                    GeometryStroke = null,
                    Fill = null,
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 }
                };
                Series.Add(ls);
                SeriesItems.Add(new TrendSeriesItem { Key = key, Name = ls.Name ?? key });
                _samplesByKey[key] = new List<(DateTime ts, double v)>();
            });
        }

        private void OnRemoved(string key)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_valuesByKey.ContainsKey(key)) _valuesByKey.Remove(key);
                if (_samplesByKey.ContainsKey(key)) _samplesByKey.Remove(key);
                // remove matching series
                for (int i = Series.Count - 1; i >= 0; i--)
                {
                    if (Series[i] is LineSeries<double> ls && (ls.Name == key || ls.Name?.Contains(key, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        Series.RemoveAt(i);
                    }
                }

                for (int i = SeriesItems.Count - 1; i >= 0; i--)
                {
                    if (SeriesItems[i].Key == key)
                    {
                        SeriesItems.RemoveAt(i);
                    }
                }
                ReleaseColor(key);
            });
        }

        private void OnSampled(string key, double value, DateTime timestampUtc)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!_valuesByKey.TryGetValue(key, out var values)) return;
                values.Add(value);
                if (_samplesByKey.TryGetValue(key, out var samples))
                {
                    samples.Add((timestampUtc, value));
                }

                // enforce retention window based on logger settings
                var maxPoints = Math.Max(1, (int)Math.Round((_loggerSvc.RetentionMinutes * 60_000.0) / Math.Max(1, _loggerSvc.SampleRateMs)));
                while (values.Count > maxPoints) values.RemoveAt(0);
                if (_samplesByKey.TryGetValue(key, out var samples2))
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-_loggerSvc.RetentionMinutes);
                    while (samples2.Count > 0 && samples2[0].ts < cutoff) samples2.RemoveAt(0);
                }

                // follow live window if enabled
                if (_followLive && Series.Count > 0)
                {
                    // use indexes as X, align window to the end
                    var count = values.Count;
                    var x = XAxes[0];
                    x.MinLimit = Math.Max(0, count - _playWindowPoints);
                    x.MaxLimit = count;
                }
            });
        }

        private bool CanDeleteSelected() => SelectedSeriesItem != null;

        private void DeleteSelected()
        {
            var item = SelectedSeriesItem;
            if (item == null) return;
            _loggerSvc.Remove(item.Key);
        }

        private void ResetView()
        {
            // reset axis limits to auto
            XAxes[0].MinLimit = null;
            XAxes[0].MaxLimit = null;
            YAxes[0].MinLimit = null;
            YAxes[0].MaxLimit = null;
        }

        private void StartFollowing()
        {
            _playWindowPoints = Math.Max(1, (int)Math.Round(60_000.0 / Math.Max(1, _loggerSvc.SampleRateMs)));
            _followLive = true;
        }

        private void StopFollowing()
        {
            _followLive = false;
        }

        private void ApplyRetention()
        {
            var mins = RetentionMinutes;
            if (mins < 1) mins = 1;
            if (mins > 60) mins = 60;
            // keep current sample rate and export folder
            _loggerSvc.UpdateSettings(mins, _loggerSvc.SampleRateMs);
            // property may have been clamped; reflect back
            RetentionMinutes = _loggerSvc.RetentionMinutes;
        }

        private SKColor AcquireColor(string key)
        {
            // pick next unused color
            for (int i = 0; i < _palette.Count; i++)
            {
                var idx = (_paletteCursor + i) % _palette.Count;
                var c = _palette[idx];
                if (!_colorByKey.Values.Contains(c))
                {
                    _paletteCursor = (idx + 1) % _palette.Count;
                    _colorByKey[key] = c;
                    return c;
                }
            }
            // all taken, reuse next
            var color = _palette[_paletteCursor];
            _paletteCursor = (_paletteCursor + 1) % _palette.Count;
            _colorByKey[key] = color;
            return color;
        }

        private void ReleaseColor(string key)
        {
            if (_colorByKey.ContainsKey(key)) _colorByKey.Remove(key);
        }

        private void ChangeColor()
        {
            var item = SelectedSeriesItem;
            if (item == null) return;
            // find series
            for (int i = 0; i < Series.Count; i++)
            {
                if (Series[i] is LineSeries<double> ls && (ls.Name == item.Name || ls.Name == item.Key))
                {
                    // assign next palette color
                    var current = _colorByKey.TryGetValue(item.Key, out var cc) ? cc : new SKColor(33,150,243);
                    var next = AcquireColor("__temp__"); // temp acquire to move cursor
                    ReleaseColor("__temp__");
                    // ensure new not equal current
                    if (next.Equals(current))
                    {
                        next = _palette[(_paletteCursor + 1) % _palette.Count];
                        _paletteCursor = (_paletteCursor + 2) % _palette.Count;
                    }
                    _colorByKey[item.Key] = next;
                    ls.Stroke = new SolidColorPaint(next) { StrokeThickness = 2 };
                    break;
                }
            }
        }
    }
}

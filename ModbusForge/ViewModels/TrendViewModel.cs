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
using System.Windows;

namespace ModbusForge.ViewModels
{
    public partial class TrendViewModel : ObservableObject
    {
        private readonly ITrendLogger _loggerSvc;
        private readonly Dictionary<string, ObservableCollection<double>> _valuesByKey = new();
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
            ResetViewCommand = new RelayCommand(ResetView);
            PlayCommand = new RelayCommand(StartFollowing);
            PauseCommand = new RelayCommand(StopFollowing);
        }

        public ObservableCollection<ISeries> Series { get; }
        public ObservableCollection<TrendSeriesItem> SeriesItems { get; }
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        private TrendSeriesItem? selectedSeriesItem;

        public IRelayCommand DeleteSelectedCommand { get; }
        public IRelayCommand ResetViewCommand { get; }
        public IRelayCommand PlayCommand { get; }
        public IRelayCommand PauseCommand { get; }

        private void OnAdded(string key, string displayName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_valuesByKey.ContainsKey(key)) return;
                var values = new ObservableCollection<double>();
                _valuesByKey[key] = values;
                var color = new SKColor(33, 150, 243);
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
            });
        }

        private void OnRemoved(string key)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_valuesByKey.TryGetValue(key, out var values))
                {
                    _valuesByKey.Remove(key);
                }
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
            });
        }

        private void OnSampled(string key, double value, DateTime timestampUtc)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!_valuesByKey.TryGetValue(key, out var values)) return;
                values.Add(value);

                // enforce retention window based on logger settings
                var maxPoints = Math.Max(1, (int)Math.Round((_loggerSvc.RetentionMinutes * 60_000.0) / Math.Max(1, _loggerSvc.SampleRateMs)));
                while (values.Count > maxPoints) values.RemoveAt(0);

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
    }
}

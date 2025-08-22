using CommunityToolkit.Mvvm.ComponentModel;
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

        public TrendViewModel(ITrendLogger loggerSvc, IOptions<LoggingSettings> options)
        {
            _loggerSvc = loggerSvc;
            var s = options?.Value ?? new LoggingSettings();
            s.Clamp();

            // Observable collection of series managed by logger events
            Series = new ObservableCollection<ISeries>();

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
        }

        public ObservableCollection<ISeries> Series { get; }
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

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
            });
        }
    }
}

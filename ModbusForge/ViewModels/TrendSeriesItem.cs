using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// One row of the trend pen list: a chart series plus the controls the
    /// user expects from a pen list (name, color, visibility, last value,
    /// delete). The view model keeps the parallel <c>Series</c> collection
    /// in sync when these properties change.
    /// </summary>
    public sealed partial class TrendSeriesItem : ObservableObject
    {
        public string Key { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AutomationLabel))]
        private string _name;

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private double? _lastValue;

        /// <summary>
        /// True while the polling loop's reads for this pen are failing.
        /// The pen list shows a red dot with the failure message; the chart
        /// keeps the last good line so the context is not lost.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AutomationLabel))]
        private bool _isFailing;

        /// <summary>Message of the most recent failed read, for the tooltip.</summary>
        [ObservableProperty]
        private string? _failureMessage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ColorHex))]
        [NotifyPropertyChangedFor(nameof(ColorBrush))]
        [NotifyPropertyChangedFor(nameof(AutomationLabel))]
        private SKColor _color;

        public TrendSeriesItem(string key, string name, SKColor color)
        {
            Key = key;
            _name = name;
            _color = color;
        }

        public string ColorHex => $"#{Color.Red:X2}{Color.Green:X2}{Color.Blue:X2}";

        public IBrush ColorBrush
        {
            get
            {
                var sk = Color;
                return new SolidColorBrush(global::Avalonia.Media.Color.FromArgb(sk.Alpha, sk.Red, sk.Green, sk.Blue));
            }
        }

        /// <summary>
        /// Automation name for the row, so assistive tech (and UI automation)
        /// reads the pen by its name instead of the type name, and hears
        /// when the pen is failing to read.
        /// </summary>
        public string AutomationLabel => IsFailing ? $"{Name} (read failing)" : Name;

        /// <summary>Wired by the view model when the pen is created.</summary>
        public IRelayCommand? RemoveCommand { get; set; }

        /// <summary>Wired by the view model when the pen is created.</summary>
        public IRelayCommand? CycleColorCommand { get; set; }
    }
}

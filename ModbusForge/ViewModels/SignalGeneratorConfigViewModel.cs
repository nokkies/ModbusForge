using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the signal generator configuration dialog.
    /// </summary>
    public sealed partial class SignalGeneratorConfigViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _waveform = "Ramp";

        [ObservableProperty]
        private int _periodMs = 1000;

        [ObservableProperty]
        private double _amplitude = 100;

        [ObservableProperty]
        private double _offset;

        public IReadOnlyList<string> Waveforms { get; } = new[] { "Ramp", "Sine", "Triangle", "Square" };

        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public bool? DialogResult { get; private set; }

        public event EventHandler? RequestClose;

        public SignalGeneratorConfigViewModel(VisualNode node)
        {
            _waveform = node.Waveform ?? "Ramp";
            _periodMs = node.PeriodMs;
            _amplitude = node.Amplitude;
            _offset = node.Offset;

            SaveCommand = new RelayCommand(Save, () => PeriodMs > 0);
            CancelCommand = new RelayCommand(Cancel);

            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PeriodMs))
                {
                    ((IRelayCommand)SaveCommand).NotifyCanExecuteChanged();
                }
            };
        }

        private void Save()
        {
            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel()
        {
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}

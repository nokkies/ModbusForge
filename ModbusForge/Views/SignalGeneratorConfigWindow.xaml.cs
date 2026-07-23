using System;
using System.Windows;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Views
{
    public partial class SignalGeneratorConfigWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly IDialogService _dialogService;

        public string SelectedWaveform { get; private set; } = "Ramp";
        public int SelectedPeriod { get; private set; } = 1000;
        public double SelectedAmplitude { get; private set; } = 100;
        public double SelectedOffset { get; private set; } = 0;

        public SignalGeneratorConfigWindow(VisualNode node, IDialogService? dialogService = null)
        {
            InitializeComponent();
            _dialogService = dialogService ?? new NullDialogService();

            WaveformCombo.ItemsSource = new[] { "Ramp", "Sine", "Triangle", "Square" };
            WaveformCombo.SelectedItem = node.Waveform ?? "Ramp";
            PeriodText.Text = node.PeriodMs.ToString();
            AmplitudeText.Text = node.Amplitude.ToString();
            OffsetText.Text = node.Offset.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (WaveformCombo.SelectedItem != null)
            {
                SelectedWaveform = WaveformCombo.SelectedItem.ToString()!;
            }

            if (!int.TryParse(PeriodText.Text, out int period) || period <= 0)
            {
                _dialogService.Show("Please enter a valid positive period in ms.", "Invalid Period", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedPeriod = period;

            if (!double.TryParse(AmplitudeText.Text, out double amplitude))
            {
                _dialogService.Show("Please enter a valid height/amplitude value.", "Invalid Height", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedAmplitude = amplitude;

            if (!double.TryParse(OffsetText.Text, out double offset))
            {
                _dialogService.Show("Please enter a valid offset value.", "Invalid Offset", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedOffset = offset;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

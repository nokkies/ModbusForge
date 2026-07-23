using Microsoft.Win32;
using ModbusForge.Models;
using ModbusForge.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace ModbusForge.Views
{
    public partial class WatchWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly TagService _tagService;
        private readonly IDialogService _dialogService;
        private readonly DispatcherTimer _updateTimer;
        private bool _isRunning = false;

        public WatchWindow(TagService tagService, IDialogService? dialogService = null)
        {
            // Assign fields BEFORE InitializeComponent so the XAML's initial SelectedIndex="2"
            // does not fire UpdateRate_Changed -> UpdateTimerInterval on null _updateTimer / _tagService.
            _tagService = tagService;
            _dialogService = dialogService ?? new NullDialogService();
            _updateTimer = new DispatcherTimer();
            _updateTimer.Tick += UpdateTimer_Tick;

            InitializeComponent();

            UpdateTimerInterval();

            // Bind to watch entries
            WatchGrid.ItemsSource = _tagService.WatchEntries;
            UpdateStatus();

            // Subscribe to collection changes
            _tagService.WatchEntries.CollectionChanged += (s, e) =>
            {
                UpdateStatus();
            };
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Check for stale entries
            var now = DateTime.Now;
            foreach (var entry in _tagService.WatchEntries)
            {
                var timeSinceUpdate = now - entry.LastUpdated;
                entry.IsStale = timeSinceUpdate.TotalMilliseconds > (entry.UpdateIntervalMs * 3);
            }

            // Refresh grid
            WatchGrid.Items.Refresh();
        }

        private void UpdateTimerInterval()
        {
            if (_updateTimer == null || _tagService == null || UpdateRateCombo == null) return;

            var selectedInterval = UpdateRateCombo.SelectedIndex switch
            {
                0 => 100,
                1 => 500,
                2 => 1000,
                3 => 2000,
                4 => 5000,
                _ => 1000
            };

            _updateTimer.Interval = TimeSpan.FromMilliseconds(selectedInterval);

            // Update all entries with new interval
            foreach (var entry in _tagService.WatchEntries)
            {
                entry.UpdateIntervalMs = selectedInterval;
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
            {
                _updateTimer.Start();
                _isRunning = true;
                StatusText.Text = "Running";
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _updateTimer.Stop();
                _isRunning = false;
                StatusText.Text = "Stopped";
            }
        }

        private void UpdateRate_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateTimerInterval();
        }

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            // Show tag browser to select a tag
            var browser = new TagBrowserWindow(_tagService, _dialogService);
            browser.Owner = this;
            browser.ShowDialog();
            UpdateStatus();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (WatchGrid.SelectedItem is WatchEntry entry)
            {
                _tagService.RemoveFromWatch(entry.Id);
                UpdateStatus();
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = _dialogService.Show("Remove all watch entries?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var entries = _tagService.WatchEntries.ToList();
                foreach (var entry in entries)
                {
                    _tagService.RemoveFromWatch(entry.Id);
                }
                UpdateStatus();
            }
        }

        private void UpdateStatus()
        {
            WatchCountText.Text = $"{_tagService.WatchEntries.Count} entries";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _updateTimer?.Stop();
            base.OnClosing(e);
        }
    }
}

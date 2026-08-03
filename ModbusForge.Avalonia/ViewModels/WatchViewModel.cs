using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public sealed partial class WatchViewModel : ObservableObject, IDisposable
    {
        private readonly TagService _tagService;
        private readonly IMessageBoxService _messageBoxService;
        private readonly ILogger<WatchViewModel> _logger;
        private readonly DispatcherTimer _updateTimer;
        private ObservableCollection<WatchEntry>? _observedEntries;
        private bool _initialized;
        private bool _disposed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStart))]
        [NotifyPropertyChangedFor(nameof(CanStop))]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopCommand))]
        private bool _isRunning;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
        private WatchEntry? _selectedEntry;

        [ObservableProperty]
        private int _selectedUpdateIntervalMs = 1000;

        [ObservableProperty]
        private string _statusMessage = "Stopped";

        public WatchViewModel(
            TagService tagService,
            IMessageBoxService messageBoxService,
            ILogger<WatchViewModel>? logger = null)
        {
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _messageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
            _logger = logger ?? NullLogger<WatchViewModel>.Instance;

            _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(SelectedUpdateIntervalMs)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            InitializeCommand = new AsyncRelayCommand(() => InitializeAsync());
            AddTagCommand = new RelayCommand(RequestTagSelectionRequested);
            RemoveCommand = new RelayCommand(RemoveSelected, CanRemoveSelected);
            ClearAllCommand = new AsyncRelayCommand(ClearAllAsync, CanClearAll);
            StartCommand = new RelayCommand(Start, () => CanStart);
            StopCommand = new RelayCommand(Stop, () => CanStop);

            _tagService.PropertyChanged += OnTagServicePropertyChanged;
            AttachEntries();
            UpdateStatus();
        }

        public IAsyncRelayCommand InitializeCommand { get; }
        public IRelayCommand AddTagCommand { get; }
        public IRelayCommand RemoveCommand { get; }
        public IAsyncRelayCommand ClearAllCommand { get; }
        public IRelayCommand StartCommand { get; }
        public IRelayCommand StopCommand { get; }

        public ObservableCollection<WatchEntry> WatchEntries => _tagService.WatchEntries;

        public IReadOnlyList<int> UpdateIntervalOptions { get; } = new[] { 100, 500, 1000, 2000, 5000 };

        public bool CanStart => !IsRunning;

        public bool CanStop => IsRunning;

        public string EntryCountText => $"{WatchEntries.Count} entries";

        public TagService TagService => _tagService;

        public IMessageBoxService MessageBoxService => _messageBoxService;

        public event EventHandler? RequestTagSelection;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || _initialized)
                return;

            try
            {
                await _tagService.InitializeAsync(cancellationToken);
                _initialized = true;
                AttachEntries();
                UpdateStatus();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Watch initialization cancelled.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to initialize the watch window");
                StatusMessage = "Watch initialization failed.";
                await _messageBoxService.ShowAsync($"Could not load watch entries: {ex.Message}", "Watch Window", DialogButton.Ok, DialogIcon.Error);
            }
        }

        public void AddTag(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId))
                return;

            try
            {
                var entry = _tagService.AddToWatch(tagId, SelectedUpdateIntervalMs);
                SelectedEntry = entry;
                UpdateStatus();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to add tag {TagId} to watch", tagId);
                _ = _messageBoxService.ShowAsync($"Could not add tag to watch: {ex.Message}", "Watch Window", DialogButton.Ok, DialogIcon.Error);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateTimer_Tick;
            _tagService.PropertyChanged -= OnTagServicePropertyChanged;
            DetachEntries();
            GC.SuppressFinalize(this);
        }

        partial void OnSelectedUpdateIntervalMsChanged(int value)
        {
            var interval = Math.Clamp(value, 100, 60000);
            if (interval != value)
            {
                SelectedUpdateIntervalMs = interval;
                return;
            }

            _updateTimer.Interval = TimeSpan.FromMilliseconds(interval);
            foreach (var entry in WatchEntries)
                entry.UpdateIntervalMs = interval;
        }

        private void RequestTagSelectionRequested() => RequestTagSelection?.Invoke(this, EventArgs.Empty);

        private void Start()
        {
            if (IsRunning)
                return;

            _updateTimer.Start();
            IsRunning = true;
            StatusMessage = "Running";
        }

        private void Stop()
        {
            if (!IsRunning)
                return;

            _updateTimer.Stop();
            IsRunning = false;
            StatusMessage = "Stopped";
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            foreach (var entry in WatchEntries)
            {
                var timeSinceUpdate = entry.LastUpdated == DateTime.MinValue
                    ? TimeSpan.MaxValue
                    : now - entry.LastUpdated;
                entry.IsStale = timeSinceUpdate.TotalMilliseconds > Math.Max(100, entry.UpdateIntervalMs) * 3;
            }
        }

        private void RemoveSelected()
        {
            if (SelectedEntry == null)
                return;

            _tagService.RemoveFromWatch(SelectedEntry.Id);
            SelectedEntry = null;
            UpdateStatus();
        }

        private async Task ClearAllAsync()
        {
            if (WatchEntries.Count == 0)
                return;

            var result = await _messageBoxService.ShowAsync(
                "Remove all watch entries?",
                "Confirm",
                DialogButton.YesNo,
                DialogIcon.Question);
            if (result != DialogResult.Yes)
                return;

            foreach (var entry in WatchEntries.ToList())
                _tagService.RemoveFromWatch(entry.Id);

            SelectedEntry = null;
            UpdateStatus();
        }

        private bool CanRemoveSelected() => SelectedEntry != null;

        private bool CanClearAll() => WatchEntries.Count > 0;

        private void UpdateStatus()
        {
            OnPropertyChanged(nameof(EntryCountText));
            OnPropertyChanged(nameof(WatchEntries));
            RemoveCommand.NotifyCanExecuteChanged();
            ClearAllCommand.NotifyCanExecuteChanged();
        }

        private void OnTagServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TagService.WatchEntries))
            {
                AttachEntries();
                UpdateStatus();
            }
        }

        private void AttachEntries()
        {
            if (_observedEntries == _tagService.WatchEntries)
                return;

            DetachEntries();
            _observedEntries = _tagService.WatchEntries;
            _observedEntries.CollectionChanged += WatchEntries_CollectionChanged;
        }

        private void DetachEntries()
        {
            if (_observedEntries != null)
                _observedEntries.CollectionChanged -= WatchEntries_CollectionChanged;
            _observedEntries = null;
        }

        private void WatchEntries_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateStatus();
        }
    }
}

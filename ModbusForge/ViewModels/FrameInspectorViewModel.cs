using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class FrameInspectorViewModel : ObservableObject, IDisposable
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly PcapImportService _pcapImportService;
        private readonly IFileDialogService? _fileDialogService;

        private readonly Queue<ModbusFrameLog> _pendingFrames = new();
        private readonly object _frameQueueLock = new();
        private int _frameFlushScheduled;
        private bool _disposed;

        /// <summary>Defensive bound on frames waiting for a UI flush.</summary>
        private const int MaxPendingFrames = 20000;

        [ObservableProperty]
        private ModbusFrameLogger? _frameLogger;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isAutoScroll = true;

        /// <summary>
        /// UI-thread mirror of the active <see cref="ModbusFrameLogger"/> ring buffer, fed by
        /// its FrameLogged event. The logger's own Frames collection is mutated from Modbus
        /// I/O threads, so it must not be bound to a DataGrid directly.
        /// </summary>
        public ObservableCollection<ModbusFrameLog> Frames { get; } = new();

        public ICommand ClearCommand { get; }
        public ICommand ImportPcapCommand { get; }

        public FrameInspectorViewModel(
            IConnectionManager connectionManager,
            IDispatcher dispatcher,
            PcapImportService pcapImportService,
            IFileDialogService? fileDialogService = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _pcapImportService = pcapImportService ?? throw new ArgumentNullException(nameof(pcapImportService));
            _fileDialogService = fileDialogService;

            ClearCommand = new RelayCommand(Clear);
            ImportPcapCommand = new AsyncRelayCommand(ImportPcapAsync);

            _connectionManager.ActiveProfileChanged += OnActiveProfileChanged;
            _connectionManager.ProfileConnected += OnProfileConnected;
            _connectionManager.ProfileDisconnected += OnProfileDisconnected;

            UpdateFrameLogger();
        }

        private void OnActiveProfileChanged(object? sender, ConnectionProfile? e)
        {
            _dispatcher.Invoke(UpdateFrameLogger);
        }

        private void OnProfileConnected(object? sender, ConnectionProfile e)
        {
            _dispatcher.Invoke(UpdateFrameLogger);
        }

        private void OnProfileDisconnected(object? sender, ConnectionProfile e)
        {
            _dispatcher.Invoke(UpdateFrameLogger);
        }

        private void UpdateFrameLogger()
        {
            if (_disposed)
                return;

            if (FrameLogger != null)
            {
                FrameLogger.FrameLogged -= OnFrameLogged;
            }

            FrameLogger = _connectionManager.ActiveService?.FrameLogger;

            // Drop any pending frames of the previous logger before importing history.
            lock (_frameQueueLock)
            {
                _pendingFrames.Clear();
            }

            if (FrameLogger != null)
            {
                // Import the history recorded before we subscribed (snapshot under the
                // logger's own lock - Frames itself is mutated from I/O threads).
                foreach (var frame in FrameLogger.Snapshot())
                {
                    lock (_frameQueueLock)
                    {
                        _pendingFrames.Enqueue(frame);
                    }
                }

                FrameLogger.FrameLogged += OnFrameLogged;
                ScheduleFrameFlush();
            }
            else
            {
                Frames.Clear();
            }

            OnPropertyChanged(nameof(FrameLogger));
        }

        private void OnFrameLogged(ModbusFrameLog frame)
        {
            if (_disposed)
                return;

            lock (_frameQueueLock)
            {
                _pendingFrames.Enqueue(frame);
                while (_pendingFrames.Count > MaxPendingFrames)
                {
                    _pendingFrames.Dequeue();
                }
            }

            ScheduleFrameFlush();
        }

        private void ScheduleFrameFlush()
        {
            if (Interlocked.Exchange(ref _frameFlushScheduled, 1) == 1)
                return; // a flush is already scheduled

            _ = _dispatcher.InvokeAsync(FlushPendingFrames);
        }

        private void FlushPendingFrames()
        {
            try
            {
                if (_disposed)
                    return;

                List<ModbusFrameLog> batch;
                lock (_frameQueueLock)
                {
                    batch = new List<ModbusFrameLog>(_pendingFrames.Count);
                    while (_pendingFrames.Count > 0)
                    {
                        batch.Add(_pendingFrames.Dequeue());
                    }
                }

                if (batch.Count > 0)
                {
                    foreach (var frame in batch)
                    {
                        Frames.Add(frame);
                    }

                    // Keep the same ring capacity as the backing logger.
                    var capacity = FrameLogger?.Capacity ?? ModbusFrameLogger.DefaultCapacity;
                    while (Frames.Count > capacity)
                    {
                        Frames.RemoveAt(0);
                    }
                }
            }
            finally
            {
                _frameFlushScheduled = 0;

                // A frame may have arrived between the snapshot and the flag reset.
                bool hasMore;
                lock (_frameQueueLock)
                {
                    hasMore = _pendingFrames.Count > 0;
                }
                if (hasMore && !_disposed)
                {
                    Interlocked.Exchange(ref _frameFlushScheduled, 1);
                    _ = _dispatcher.InvokeAsync(FlushPendingFrames);
                }
            }
        }

        private void Clear()
        {
            FrameLogger?.Clear();
            lock (_frameQueueLock)
            {
                _pendingFrames.Clear();
            }
            Frames.Clear();
            StatusMessage = "Frames cleared.";
        }

        private async Task ImportPcapAsync()
        {
            if (_fileDialogService is null)
            {
                StatusMessage = "File dialog service not available.";
                return;
            }

            var path = await _fileDialogService.ShowOpenFileDialogAsync("Import Pcap", "Pcap files (*.pcap;*.pcapng)|*.pcap;*.pcapng|All files (*.*)|*.*");
            if (path is null)
            {
                return;
            }

            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (FrameLogger is null)
                    {
                        FrameLogger = new ModbusFrameLogger();
                        FrameLogger.FrameLogged += OnFrameLogged;
                    }
                });

                await Task.Run(() =>
                {
                    var result = _pcapImportService.Import(path);

                    _dispatcher.Invoke(() =>
                    {
                        if (_disposed)
                            return;

                        if (result.Frames.Count > 0 && FrameLogger != null)
                        {
                            foreach (var frame in result.Frames)
                            {
                                FrameLogger.Log(frame);
                            }
                        }

                        StatusMessage = result.Message;
                    });
                });
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                StatusMessage = $"Pcap import failed: {ex.Message}";
            }
        }

        public void Dispose()
        {
            _disposed = true;

            // The connection manager is a long-lived singleton: unsubscribe or it keeps
            // this view model (and the per-service frame logger) referenced forever.
            _connectionManager.ActiveProfileChanged -= OnActiveProfileChanged;
            _connectionManager.ProfileConnected -= OnProfileConnected;
            _connectionManager.ProfileDisconnected -= OnProfileDisconnected;
            if (FrameLogger != null)
            {
                FrameLogger.FrameLogged -= OnFrameLogged;
            }
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class FrameInspectorViewModel : ObservableObject
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly PcapImportService _pcapImportService;
        private readonly IFileDialogService? _fileDialogService;

        [ObservableProperty]
        private ModbusFrameLogger? _frameLogger;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isAutoScroll = true;

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
            // The logger is a stable per-service instance: re-reading it on every
            // connection event keeps the inspector pointed at the live log without
            // ever clearing the ring buffer (a reconnect is not a user-initiated clear).
            // When no service exists yet (nothing connected, nothing created) the
            // inspector falls back to its own in-memory log so pcap imports still work.
            var logger = _connectionManager.ActiveService?.FrameLogger;
            if (logger == null && FrameLogger == null)
            {
                FrameLogger = new ModbusFrameLogger();
            }
            else if (!ReferenceEquals(FrameLogger, logger))
            {
                FrameLogger = logger;
            }
        }

        private void Clear()
        {
            FrameLogger?.Clear();
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
                    }
                });

                await Task.Run(() =>
                {
                    var result = _pcapImportService.Import(path);

                    _dispatcher.Invoke(() =>
                    {
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
    }
}

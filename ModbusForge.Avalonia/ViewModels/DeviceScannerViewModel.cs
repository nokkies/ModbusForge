using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// Avalonia port of the device scanner. Sweeps an IP range for Modbus units and exposes the results.
    /// </summary>
    public sealed partial class DeviceScannerViewModel : ObservableObject, IDisposable
    {
        private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

        private readonly IDeviceScannerService _scannerService;
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly IFileDialogService _fileDialogService;
        private readonly IMessageBoxService _messageBoxService;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<DeviceScannerViewModel> _logger;
        private CancellationTokenSource? _scanCancellation;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearResultsCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
        private bool _isScanning;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddToProfilesCommand))]
        private DeviceScanResult? _selectedDevice;

        [ObservableProperty]
        private double _progressPercent;

        [ObservableProperty]
        private string _statusMessage = "Ready.";

        [ObservableProperty]
        private string _startIpAddress = "127.0.0.1";

        [ObservableProperty]
        private string _endIpAddress = "127.0.0.1";

        [ObservableProperty]
        private int _startPort = 502;

        [ObservableProperty]
        private int _endPort = 502;

        [ObservableProperty]
        private int _startUnitId = DeviceScanOptions.MinUnitId;

        [ObservableProperty]
        private int _endUnitId = DeviceScanOptions.MinUnitId;

        [ObservableProperty]
        private int _connectTimeoutMs = 500;

        [ObservableProperty]
        private int _responseTimeoutMs = 1000;

        [ObservableProperty]
        private int _maxConcurrency = 16;

        [ObservableProperty]
        private ScanRegisterType _registerType = ScanRegisterType.HoldingRegisters;

        [ObservableProperty]
        private int _probeAddress = 1;

        [ObservableProperty]
        private bool _scanRegisterRange;

        [ObservableProperty]
        private int _registerScanStartAddress = 1;

        [ObservableProperty]
        private int _registerScanCount = 16;

        [ObservableProperty]
        private bool _readDeviceIdentification = true;

        [ObservableProperty]
        private bool _detectFunctionCodes = true;

        public ObservableCollection<DeviceScanResult> Devices { get; } = new();

        public IReadOnlyList<ScanRegisterType> RegisterTypes { get; } = new[]
        {
            ScanRegisterType.HoldingRegisters,
            ScanRegisterType.InputRegisters,
            ScanRegisterType.Coils,
            ScanRegisterType.DiscreteInputs
        };

        public DeviceScannerViewModel(
            IDeviceScannerService scannerService,
            IConnectionManager connectionManager,
            IDispatcher dispatcher,
            IFileDialogService fileDialogService,
            IMessageBoxService messageBoxService,
            IFileSystem fileSystem,
            ILogger<DeviceScannerViewModel> logger)
        {
            _scannerService = scannerService ?? throw new ArgumentNullException(nameof(scannerService));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _messageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            StartScanCommand = new AsyncRelayCommand(StartScanAsync, () => !IsScanning);
            CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
            ClearResultsCommand = new RelayCommand(ClearResults, () => !IsScanning && Devices.Count > 0);
            ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => !IsScanning && Devices.Count > 0);
            AddToProfilesCommand = new RelayCommand(AddSelectedToProfiles, () => SelectedDevice != null);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        }

        public event EventHandler? RequestClose;

        public IAsyncRelayCommand StartScanCommand { get; }
        public IRelayCommand CancelScanCommand { get; }
        public IRelayCommand ClearResultsCommand { get; }
        public IAsyncRelayCommand ExportCsvCommand { get; }
        public IRelayCommand AddToProfilesCommand { get; }
        public IRelayCommand CloseCommand { get; }

        partial void OnIsScanningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanStartScan));
            OnPropertyChanged(nameof(CanCancelScan));
            OnPropertyChanged(nameof(CanClearResults));
            OnPropertyChanged(nameof(CanExportCsv));
        }

        public bool CanStartScan => !IsScanning;
        public bool CanCancelScan => IsScanning;
        public bool CanClearResults => !IsScanning && Devices.Count > 0;
        public bool CanExportCsv => !IsScanning && Devices.Count > 0;

        public DeviceScanOptions BuildOptions() => new()
        {
            StartIpAddress = StartIpAddress,
            EndIpAddress = EndIpAddress,
            StartPort = StartPort,
            EndPort = EndPort,
            StartUnitId = (byte)StartUnitId,
            EndUnitId = (byte)EndUnitId,
            ConnectTimeoutMs = ConnectTimeoutMs,
            ResponseTimeoutMs = ResponseTimeoutMs,
            MaxConcurrency = MaxConcurrency,
            RegisterType = RegisterType,
            ProbeAddress = ProbeAddress,
            ScanRegisterRange = ScanRegisterRange,
            RegisterScanStartAddress = RegisterScanStartAddress,
            RegisterScanCount = RegisterScanCount,
            ReadDeviceIdentification = ReadDeviceIdentification,
            DetectFunctionCodes = DetectFunctionCodes
        };

        private async Task StartScanAsync()
        {
            var options = BuildOptions();
            var validationError = _scannerService.Validate(options);
            if (validationError != null)
            {
                StatusMessage = validationError;
                _logger.LogWarning("Invalid scan settings: {Error}", validationError);
                await _messageBoxService.ShowAsync(validationError, "Invalid scan settings", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            Devices.Clear();
            ProgressPercent = 0;
            IsScanning = true;
            StatusMessage = "Scanning...";

            _scanCancellation?.Dispose();
            _scanCancellation = new CancellationTokenSource();

            var progress = new Progress<DeviceScanProgress>(OnProgress);

            try
            {
                var results = await _scannerService.ScanAsync(options, progress, OnDeviceFound, _scanCancellation.Token).ConfigureAwait(true);
                StatusMessage = $"Scan complete: {Devices.Count} device(s) found across {results.Count} probe(s).";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"Scan cancelled after finding {Devices.Count} device(s).";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Device scan failed");
                StatusMessage = $"Scan failed: {ex.Message}";
                await _messageBoxService.ShowAsync(ex.Message, "Scan failed", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsScanning = false;
                RefreshCommandStates();
            }
        }

        private void OnProgress(DeviceScanProgress progress)
        {
            ProgressPercent = progress.PercentComplete;
            StatusMessage = $"Scanning {progress.CurrentTarget} ({progress.Completed}/{progress.Total}) - {progress.DevicesFound} device(s) found.";
        }

        private void OnDeviceFound(DeviceScanResult device)
        {
            _dispatcher.Invoke(() =>
            {
                Devices.Add(device);
                RefreshCommandStates();
            });
        }

        private void AddSelectedToProfiles()
        {
            var device = SelectedDevice;
            if (device == null) return;

            var existing = _connectionManager.Profiles.FirstOrDefault(p =>
                p.IpAddress == device.IpAddress && p.Port == device.Port && p.UnitId == device.UnitId);

            if (existing != null)
            {
                StatusMessage = $"{device.Endpoint} unit {device.UnitId} is already saved as '{existing.Name}'.";
                return;
            }

            var name = string.IsNullOrWhiteSpace(device.VendorName)
                ? $"{device.IpAddress}:{device.Port} #{device.UnitId}"
                : $"{device.VendorName} {device.IpAddress}:{device.Port} #{device.UnitId}";

            _connectionManager.AddProfile(new ConnectionProfile(name, device.IpAddress, device.Port, device.UnitId));
            _connectionManager.SaveProfiles();
            _logger.LogInformation("Added scanned device {Endpoint} unit {UnitId} to connection profiles", device.Endpoint, device.UnitId);
            StatusMessage = $"Added '{name}' to connection profiles.";
        }

        private void CancelScan()
        {
            _scanCancellation?.Cancel();
            StatusMessage = "Cancelling...";
        }

        private void ClearResults()
        {
            Devices.Clear();
            SelectedDevice = null;
            ProgressPercent = 0;
            StatusMessage = "Ready.";
            RefreshCommandStates();
        }

        private async Task ExportCsvAsync()
        {
            var path = await _fileDialogService.ShowSaveFileDialogAsync("Export scan results", CsvFilter, "modbus-scan.csv").ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                await _fileSystem.WriteAllTextAsync(path, BuildCsv()).ConfigureAwait(true);
                StatusMessage = $"Exported {Devices.Count} device(s) to {path}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to export scan results to {Path}", path);
                StatusMessage = $"Export failed: {ex.Message}";
                await _messageBoxService.ShowAsync(ex.Message, "Export failed", DialogButton.Ok, DialogIcon.Error);
            }
        }

        internal string BuildCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("IpAddress,Port,UnitId,Status,LatencyMs,FunctionCodes,Message,RegisterAddress,RegisterValue");

            foreach (var device in Devices)
            {
                if (device.Registers.Count == 0)
                {
                    builder.AppendLine(string.Join(',',
                        Escape(device.IpAddress),
                        device.Port.ToString(CultureInfo.InvariantCulture),
                        device.UnitId.ToString(CultureInfo.InvariantCulture),
                        Escape(device.Status.ToString()),
                        device.LatencyMs.ToString(CultureInfo.InvariantCulture),
                        Escape(device.SupportedFunctionCodesText),
                        Escape(device.Message),
                        string.Empty,
                        string.Empty));
                }
                else
                {
                    foreach (var register in device.Registers)
                    {
                        builder.AppendLine(string.Join(',',
                            Escape(device.IpAddress),
                            device.Port.ToString(CultureInfo.InvariantCulture),
                            device.UnitId.ToString(CultureInfo.InvariantCulture),
                            Escape(device.Status.ToString()),
                            device.LatencyMs.ToString(CultureInfo.InvariantCulture),
                            Escape(device.SupportedFunctionCodesText),
                            Escape(register.Error),
                            register.Address.ToString(CultureInfo.InvariantCulture),
                            register.IsReadable ? register.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
                    }
                }
            }

            return builder.ToString();
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        private void RefreshCommandStates()
        {
            StartScanCommand.NotifyCanExecuteChanged();
            CancelScanCommand.NotifyCanExecuteChanged();
            ClearResultsCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();
            AddToProfilesCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(CanStartScan));
            OnPropertyChanged(nameof(CanCancelScan));
            OnPropertyChanged(nameof(CanClearResults));
            OnPropertyChanged(nameof(CanExportCsv));
        }

        public void Dispose()
        {
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }
}

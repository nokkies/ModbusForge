using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    /// <summary>
    /// Drives the device scanner window: validates the scan options, runs the scan and
    /// exposes discovered devices for display and CSV export.
    /// </summary>
    public class DeviceScannerViewModel : ViewModelBase
    {
        private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

        private readonly IDeviceScannerService _scannerService;
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly IDialogService _dialogService;
        private readonly IFileDialogService _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<DeviceScannerViewModel> _logger;

        private CancellationTokenSource? _scanCancellation;

        private string _startIpAddress = "127.0.0.1";
        private string _endIpAddress = "127.0.0.1";
        private int _startPort = 502;
        private int _endPort = 502;
        private byte _startUnitId = DeviceScanOptions.MinUnitId;
        private byte _endUnitId = DeviceScanOptions.MinUnitId;
        private int _connectTimeoutMs = 500;
        private int _responseTimeoutMs = 1000;
        private int _maxConcurrency = 16;
        private ScanRegisterType _registerType = ScanRegisterType.HoldingRegisters;
        private int _probeAddress;
        private bool _scanRegisterRange;
        private int _registerScanStartAddress;
        private int _registerScanCount = 16;
        private bool _readDeviceIdentification = true;

        private bool _isScanning;
        private double _progressPercent;
        private string _statusMessage = "Ready.";
        private DeviceScanResult? _selectedDevice;

        public DeviceScannerViewModel(
            IDeviceScannerService scannerService,
            IConnectionManager connectionManager,
            IDispatcher dispatcher,
            IDialogService dialogService,
            IFileDialogService fileDialogService,
            IFileSystem fileSystem,
            ILogger<DeviceScannerViewModel> logger)
        {
            _scannerService = scannerService ?? throw new ArgumentNullException(nameof(scannerService));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
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

        public ObservableCollection<DeviceScanResult> Devices { get; } = new();

        public IReadOnlyList<ScanRegisterType> RegisterTypes { get; } = new[]
        {
            ScanRegisterType.HoldingRegisters,
            ScanRegisterType.InputRegisters,
            ScanRegisterType.Coils,
            ScanRegisterType.DiscreteInputs
        };

        public IAsyncRelayCommand StartScanCommand { get; }
        public IRelayCommand CancelScanCommand { get; }
        public IRelayCommand ClearResultsCommand { get; }
        public IAsyncRelayCommand ExportCsvCommand { get; }
        public IRelayCommand AddToProfilesCommand { get; }
        public IRelayCommand CloseCommand { get; }

        public string StartIpAddress
        {
            get => _startIpAddress;
            set => SetProperty(ref _startIpAddress, value);
        }

        public string EndIpAddress
        {
            get => _endIpAddress;
            set => SetProperty(ref _endIpAddress, value);
        }

        public int StartPort
        {
            get => _startPort;
            set => SetProperty(ref _startPort, value);
        }

        public int EndPort
        {
            get => _endPort;
            set => SetProperty(ref _endPort, value);
        }

        public byte StartUnitId
        {
            get => _startUnitId;
            set => SetProperty(ref _startUnitId, value);
        }

        public byte EndUnitId
        {
            get => _endUnitId;
            set => SetProperty(ref _endUnitId, value);
        }

        public int ConnectTimeoutMs
        {
            get => _connectTimeoutMs;
            set => SetProperty(ref _connectTimeoutMs, value);
        }

        public int ResponseTimeoutMs
        {
            get => _responseTimeoutMs;
            set => SetProperty(ref _responseTimeoutMs, value);
        }

        public int MaxConcurrency
        {
            get => _maxConcurrency;
            set => SetProperty(ref _maxConcurrency, value);
        }

        public ScanRegisterType RegisterType
        {
            get => _registerType;
            set => SetProperty(ref _registerType, value);
        }

        public int ProbeAddress
        {
            get => _probeAddress;
            set => SetProperty(ref _probeAddress, value);
        }

        public bool ScanRegisterRange
        {
            get => _scanRegisterRange;
            set => SetProperty(ref _scanRegisterRange, value);
        }

        public int RegisterScanStartAddress
        {
            get => _registerScanStartAddress;
            set => SetProperty(ref _registerScanStartAddress, value);
        }

        public int RegisterScanCount
        {
            get => _registerScanCount;
            set => SetProperty(ref _registerScanCount, value);
        }

        public bool ReadDeviceIdentification
        {
            get => _readDeviceIdentification;
            set => SetProperty(ref _readDeviceIdentification, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    RefreshCommandStates();
                }
            }
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetProperty(ref _progressPercent, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public DeviceScanResult? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    AddToProfilesCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public DeviceScanOptions BuildOptions() => new()
        {
            StartIpAddress = StartIpAddress,
            EndIpAddress = EndIpAddress,
            StartPort = StartPort,
            EndPort = EndPort,
            StartUnitId = StartUnitId,
            EndUnitId = EndUnitId,
            ConnectTimeoutMs = ConnectTimeoutMs,
            ResponseTimeoutMs = ResponseTimeoutMs,
            MaxConcurrency = MaxConcurrency,
            RegisterType = RegisterType,
            ProbeAddress = ProbeAddress,
            ScanRegisterRange = ScanRegisterRange,
            RegisterScanStartAddress = RegisterScanStartAddress,
            RegisterScanCount = RegisterScanCount,
            ReadDeviceIdentification = ReadDeviceIdentification
        };

        private async Task StartScanAsync()
        {
            var options = BuildOptions();
            var validationError = _scannerService.Validate(options);
            if (validationError != null)
            {
                StatusMessage = validationError;
                _dialogService.Show(validationError, "Invalid scan settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
                _dialogService.Show(ex.Message, "Scan failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            if (device == null)
            {
                return;
            }

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
            var path = _fileDialogService.ShowSaveFileDialog("Export scan results", CsvFilter, "modbus-scan.csv");
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
                _dialogService.Show(ex.Message, "Export failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        internal string BuildCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("IpAddress,Port,UnitId,Status,LatencyMs,Message,RegisterAddress,RegisterValue");

            foreach (var device in Devices)
            {
                builder.AppendLine(string.Join(',',
                    Escape(device.IpAddress),
                    device.Port.ToString(CultureInfo.InvariantCulture),
                    device.UnitId.ToString(CultureInfo.InvariantCulture),
                    device.Status.ToString(),
                    device.LatencyMs.ToString(CultureInfo.InvariantCulture),
                    Escape(device.Message),
                    string.Empty,
                    string.Empty));

                foreach (var register in device.Registers)
                {
                    builder.AppendLine(string.Join(',',
                        Escape(device.IpAddress),
                        device.Port.ToString(CultureInfo.InvariantCulture),
                        device.UnitId.ToString(CultureInfo.InvariantCulture),
                        register.IsReadable ? "RegisterRead" : "RegisterUnreadable",
                        string.Empty,
                        Escape(register.Error),
                        register.Address.ToString(CultureInfo.InvariantCulture),
                        register.IsReadable ? register.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scanCancellation?.Cancel();
                _scanCancellation?.Dispose();
                _scanCancellation = null;
            }

            base.Dispose(disposing);
        }
    }
}

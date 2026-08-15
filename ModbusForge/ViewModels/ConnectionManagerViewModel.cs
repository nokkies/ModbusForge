using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Models;
using ModbusForge.Avalonia.Services;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// Avalonia port of the connection manager. Manages Modbus connection profiles and their TCP/serial settings.
    /// </summary>
    public sealed partial class ConnectionManagerViewModel : ObservableObject, IDisposable
    {
        public static IReadOnlyList<TransportType> TransportOptions { get; } =
            new[] { TransportType.Tcp, TransportType.Rtu, TransportType.Ascii };

        public static IReadOnlyList<Parity> ParityOptions { get; } =
            new[] { Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space };

        public static IReadOnlyList<StopBits> StopBitsOptions { get; } =
            new[] { StopBits.None, StopBits.One, StopBits.OnePointFive, StopBits.Two };

        public static IReadOnlyList<int> BaudRates { get; } = SerialSettingsDetector.CommonBaudRates.OrderBy(b => b).ToList();

        public bool IsSerial => SelectedProfile?.Transport is TransportType.Rtu or TransportType.Ascii;

        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly IMessageBoxService _messageBoxService;
        private readonly ILogger<ConnectionManagerViewModel>? _logger;
        private readonly SerialSettingsDetector _detector = new();
        private CancellationTokenSource? _autoDetectCts;
        private CancellationTokenSource? _refreshSerialPortsCts;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
        [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(AutoDetectSettingsCommand))]
        private ConnectionProfile? _selectedProfile;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(AutoDetectSettingsCommand))]
        private bool _isConnecting;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AutoDetectSettingsCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelAutoDetectCommand))]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool _isAutoDetecting;

        [ObservableProperty]
        private bool _isCustomPortSelected;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private SerialPortInfo? _selectedSerialPort;

        public ObservableCollection<ConnectionProfile> Profiles => _connectionManager.Profiles;

        public ObservableCollection<SerialPortInfo> AvailableSerialPorts { get; } = new();

        public bool HasSelection => SelectedProfile != null;

        public bool CanRemove => HasSelection && Profiles.Count > 1;

        public bool CanClone => HasSelection;

        public ConnectionManagerViewModel(IConnectionManager connectionManager, IDispatcher dispatcher, IMessageBoxService messageBoxService, ILogger<ConnectionManagerViewModel>? logger = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _messageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
            _logger = logger;

            AddCommand = new RelayCommand(AddProfile);
            RemoveCommand = new RelayCommand(RemoveProfile, () => CanRemove);
            CloneCommand = new RelayCommand(CloneProfile, () => CanClone);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => CanConnect());
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => CanDisconnect());
            SaveCommand = new RelayCommand(SaveAndClose);
            CancelCommand = new RelayCommand(Cancel);
            AutoDetectSettingsCommand = new AsyncRelayCommand(AutoDetectSettingsAsync, () => CanAutoDetect());
            CancelAutoDetectCommand = new RelayCommand(CancelAutoDetect, () => IsAutoDetecting);

            if (_connectionManager.Profiles.Count > 0)
            {
                SelectedProfile = _connectionManager.ActiveProfile ?? _connectionManager.Profiles[0];
            }
            else
            {
                _ = RefreshSerialPortsAsync();
            }

            _connectionManager.Profiles.CollectionChanged += Profiles_CollectionChanged;
            _connectionManager.ActiveProfileChanged += ConnectionManager_ActiveProfileChanged;
        }

        public IRelayCommand AddCommand { get; }
        public IRelayCommand RemoveCommand { get; }
        public IRelayCommand CloneCommand { get; }
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IAsyncRelayCommand AutoDetectSettingsCommand { get; }
        public IRelayCommand CancelAutoDetectCommand { get; }

        public event EventHandler<bool>? RequestClose;

        partial void OnSelectedProfileChanged(ConnectionProfile? value)
        {
            if (SelectedProfile != null)
            {
                SelectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
            }

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSerial));
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanClone));
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
            AutoDetectSettingsCommand.NotifyCanExecuteChanged();

            _ = RefreshSerialPortsAsync();
        }

        partial void OnSelectedProfileChanging(ConnectionProfile? value)
        {
            if (SelectedProfile != null)
            {
                SelectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }
        }

        partial void OnSelectedSerialPortChanged(SerialPortInfo? value)
        {
            if (value != null && SelectedProfile != null)
            {
                if (!value.IsCustom && !string.Equals(SelectedProfile.ComPort, value.PortName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedProfile.ComPort = value.PortName;
                }

                IsCustomPortSelected = value.IsCustom;
            }
            else
            {
                IsCustomPortSelected = false;
            }
        }

        private bool CanConnect() => HasSelection && !IsConnecting && !IsAutoDetecting && SelectedProfile is { IsConnected: false };

        private bool CanDisconnect() => HasSelection && !IsConnecting && !IsAutoDetecting && SelectedProfile is { IsConnected: true };

        private bool CanAutoDetect() => HasSelection && !IsAutoDetecting && IsSerial && !string.IsNullOrWhiteSpace(SelectedProfile?.ComPort);

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(CanDisconnect));
            }
            else if (e.PropertyName == nameof(ConnectionProfile.Transport))
            {
                OnPropertyChanged(nameof(IsSerial));
                AutoDetectSettingsCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(ConnectionProfile.ComPort))
            {
                AutoDetectSettingsCommand.NotifyCanExecuteChanged();
                SyncSelectedSerialPort();
            }
        }

        private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanRemove));
        }

        private void ConnectionManager_ActiveProfileChanged(object? sender, ConnectionProfile? e)
        {
            _dispatcher.Invoke(() => SelectedProfile = e);
        }

        private async Task RefreshSerialPortsAsync()
        {
            _refreshSerialPortsCts?.Cancel();
            _refreshSerialPortsCts?.Dispose();
            _refreshSerialPortsCts = new CancellationTokenSource();
            var token = _refreshSerialPortsCts.Token;

            try
            {
                var ports = await Task.Run(() => SerialPort.GetPortNames()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList(), token);

                await _dispatcher.InvokeAsync(() =>
                {
                    AvailableSerialPorts.Clear();
                    foreach (var port in ports)
                    {
                        AvailableSerialPorts.Add(new SerialPortInfo(port));
                    }

                    AvailableSerialPorts.Add(new SerialPortInfo("Custom port...", isCustom: true));
                    SyncSelectedSerialPort();
                });

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return;
                }

                var descriptions = await Task.Run(() =>
#pragma warning disable CA1416
                    GetSerialPortDescriptions(),
#pragma warning restore CA1416
                    token);

                if (descriptions == null || token.IsCancellationRequested)
                {
                    return;
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    var previousPortName = SelectedSerialPort?.PortName;

                    AvailableSerialPorts.Clear();
                    foreach (var port in descriptions.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        AvailableSerialPorts.Add(new SerialPortInfo(port, descriptions[port]));
                    }

                    AvailableSerialPorts.Add(new SerialPortInfo("Custom port...", isCustom: true));

                    if (!string.IsNullOrWhiteSpace(previousPortName))
                    {
                        var matching = AvailableSerialPorts.FirstOrDefault(p =>
                            !p.IsCustom && string.Equals(p.PortName, previousPortName, StringComparison.OrdinalIgnoreCase));
                        if (matching != null)
                        {
                            SelectedSerialPort = matching;
                            return;
                        }
                    }

                    SyncSelectedSerialPort();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to refresh serial ports");
            }
        }

        [SupportedOSPlatform("windows")]
        private static Dictionary<string, string> GetSerialPortDescriptions()
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return descriptions;
            }

            try
            {
                using var pnpSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                pnpSearcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var o in pnpSearcher.Get())
                {
                    using var mo = (ManagementObject)o;
                    var name = mo["Name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var match = Regex.Match(name, @"\(COM\d+\)");
                    if (match.Success)
                    {
                        var port = match.Value.Trim('(', ')');
                        var description = Regex.Replace(name, @"\s*\(COM\d+\)", string.Empty).Trim();
                        descriptions[port] = description;
                    }
                }
            }
            catch
            {
                // Fall through to plain COM port names.
            }

            try
            {
                using var serialSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
                serialSearcher.Options.Timeout = TimeSpan.FromSeconds(5);
                foreach (var o in serialSearcher.Get())
                {
                    using var mo = (ManagementObject)o;
                    var deviceId = mo["DeviceID"]?.ToString();
                    var description = mo["Description"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(deviceId) &&
                        !descriptions.ContainsKey(deviceId) &&
                        !string.IsNullOrWhiteSpace(description))
                    {
                        descriptions[deviceId] = description;
                    }
                }
            }
            catch
            {
                // Fall through to plain COM port names.
            }

            foreach (var port in SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                descriptions.TryAdd(port, string.Empty);
            }

            return descriptions;
        }

        private void SyncSelectedSerialPort()
        {
            if (SelectedProfile == null)
            {
                SelectedSerialPort = null;
                return;
            }

            var matching = AvailableSerialPorts.FirstOrDefault(p =>
                !p.IsCustom && string.Equals(p.PortName, SelectedProfile.ComPort, StringComparison.OrdinalIgnoreCase));

            SelectedSerialPort = matching ?? AvailableSerialPorts.FirstOrDefault(p => p.IsCustom);
            IsCustomPortSelected = SelectedSerialPort?.IsCustom ?? false;
        }

        private void AddProfile()
        {
            var newProfile = new ConnectionProfile($"Connection {Profiles.Count + 1}", "127.0.0.1", 502, 1);
            _connectionManager.AddProfile(newProfile);
            _connectionManager.SetActiveProfile(newProfile);
            SelectedProfile = newProfile;
        }

        private void RemoveProfile()
        {
            if (SelectedProfile == null || Profiles.Count <= 1)
            {
                return;
            }

            var toRemove = SelectedProfile;
            var index = Profiles.IndexOf(toRemove);
            var nextIndex = index == 0 && Profiles.Count > 1 ? 1 : 0;
            var next = Profiles[nextIndex] != toRemove ? Profiles[nextIndex] : null;
            _connectionManager.RemoveProfile(toRemove);
            SelectedProfile = next ?? Profiles.FirstOrDefault();
        }

        private void CloneProfile()
        {
            if (SelectedProfile == null) return;

            var cloned = SelectedProfile.Clone();
            _connectionManager.AddProfile(cloned);
            SelectedProfile = cloned;
        }

        private async Task ConnectAsync()
        {
            if (SelectedProfile == null || IsConnecting) return;

            // Validate the edited fields before touching the network - an invalid IP,
            // port, or unit list used to fail only deep in the socket code.
            var validationError = GetProfileValidationError(SelectedProfile);
            if (!string.IsNullOrEmpty(validationError))
            {
                StatusMessage = validationError;
                return;
            }

            IsConnecting = true;
            StatusMessage = $"Connecting to {SelectedProfile.DisplayName}...";

            try
            {
                var success = await _connectionManager.ConnectProfileAsync(SelectedProfile);
                StatusMessage = success ? "Connected." : "Connection failed.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsConnecting = false;
            }
        }

        /// <summary>First validation error of the transport-relevant profile fields, or empty.</summary>
        private static string GetProfileValidationError(ConnectionProfile profile)
        {
            var fields = profile.Transport == TransportType.Tcp
                ? new[] { nameof(ConnectionProfile.IpAddress), nameof(ConnectionProfile.Port) }
                : new[] { nameof(ConnectionProfile.BaudRate) };

            if (profile.IsServerMode)
                fields = fields.Append(nameof(ConnectionProfile.ServerUnitIds)).ToArray();

            foreach (var field in fields)
            {
                var fieldError = profile[field];
                if (!string.IsNullOrEmpty(fieldError))
                    return fieldError;
            }

            return string.Empty;
        }

        private async Task DisconnectAsync()
        {
            if (SelectedProfile == null || IsConnecting) return;

            IsConnecting = true;
            StatusMessage = $"Disconnecting from {SelectedProfile.DisplayName}...";

            try
            {
                await _connectionManager.DisconnectProfileAsync(SelectedProfile);
                StatusMessage = "Disconnected.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
            }
            finally
            {
                IsConnecting = false;
            }
        }

        private async Task AutoDetectSettingsAsync()
        {
            if (SelectedProfile == null || IsAutoDetecting) return;

            _autoDetectCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            IsAutoDetecting = true;
            StatusMessage = "Auto-detecting serial settings...";

            try
            {
                var progress = new Progress<string>(m => StatusMessage = m);
                var result = await _detector.DetectAsync(SelectedProfile, progress, _autoDetectCts.Token);

                if (result.Found && SelectedProfile != null)
                {
                    SelectedProfile.BaudRate = result.BaudRate;
                    SelectedProfile.Parity = result.Parity;
                    SelectedProfile.DataBits = result.DataBits;
                    SelectedProfile.StopBits = result.StopBits;
                }

                StatusMessage = result.Summary;
                await _messageBoxService.ShowAsync(result.Log, "Auto-Detect Result", DialogButton.Ok, DialogIcon.Information);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Auto-detect cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Auto-detect error: {ex.Message}";
            }
            finally
            {
                IsAutoDetecting = false;
                _autoDetectCts?.Dispose();
                _autoDetectCts = null;
            }
        }

        private void CancelAutoDetect()
        {
            _autoDetectCts?.Cancel();
        }

        private void SaveAndClose()
        {
            _connectionManager.SaveProfiles();
            RequestClose?.Invoke(this, true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(this, false);
        }

        public void Dispose()
        {
            _connectionManager.Profiles.CollectionChanged -= Profiles_CollectionChanged;
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;
            _autoDetectCts?.Dispose();
            _refreshSerialPortsCts?.Cancel();
            _refreshSerialPortsCts?.Dispose();

            if (SelectedProfile != null)
            {
                SelectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }
        }
    }
}

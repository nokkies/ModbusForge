using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public sealed partial class DecodeViewModel : ObservableObject, IDisposable
    {
        public const int MaxDecodeRegisters = 2;
        private const int MinimumDecodeRegisters = 1;
        private const int MaximumAddress = 0xFFFF;

        private readonly IConnectionManager _connectionManager;
        private readonly IMessageBoxService? _messageBoxService;
        private readonly ILogger<DecodeViewModel> _logger;
        private ConnectionProfile? _subscribedProfile;
        private bool _disposed;

        [ObservableProperty]
        private string _area = "HoldingRegister";

        [ObservableProperty]
        private int _address = 1;

        [ObservableProperty]
        private string _addressInput = "1";

        [ObservableProperty]
        private int _readCount = MaxDecodeRegisters;

        [ObservableProperty]
        private bool _swapBytes;

        [ObservableProperty]
        private bool _swapWords;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty] private string _raw16HexNone = string.Empty;
        [ObservableProperty] private string _uint16TextNone = string.Empty;
        [ObservableProperty] private string _int16TextNone = string.Empty;
        [ObservableProperty] private string _ascii2TextNone = string.Empty;

        [ObservableProperty] private string _raw16HexSwapB = string.Empty;
        [ObservableProperty] private string _uint16TextSwapB = string.Empty;
        [ObservableProperty] private string _int16TextSwapB = string.Empty;
        [ObservableProperty] private string _ascii2TextSwapB = string.Empty;

        [ObservableProperty] private string _raw16HexSwapW = string.Empty;
        [ObservableProperty] private string _uint16TextSwapW = string.Empty;
        [ObservableProperty] private string _int16TextSwapW = string.Empty;
        [ObservableProperty] private string _ascii2TextSwapW = string.Empty;

        [ObservableProperty] private string _raw16HexSwapBW = string.Empty;
        [ObservableProperty] private string _uint16TextSwapBW = string.Empty;
        [ObservableProperty] private string _int16TextSwapBW = string.Empty;
        [ObservableProperty] private string _ascii2TextSwapBW = string.Empty;

        [ObservableProperty] private string _raw32HexNone = string.Empty;
        [ObservableProperty] private string _uint32TextNone = string.Empty;
        [ObservableProperty] private string _int32TextNone = string.Empty;
        [ObservableProperty] private string _float32TextNone = string.Empty;
        [ObservableProperty] private string _ascii4TextNone = string.Empty;

        [ObservableProperty] private string _raw32HexSwapB = string.Empty;
        [ObservableProperty] private string _uint32TextSwapB = string.Empty;
        [ObservableProperty] private string _int32TextSwapB = string.Empty;
        [ObservableProperty] private string _float32TextSwapB = string.Empty;
        [ObservableProperty] private string _ascii4TextSwapB = string.Empty;

        [ObservableProperty] private string _raw32HexSwapW = string.Empty;
        [ObservableProperty] private string _uint32TextSwapW = string.Empty;
        [ObservableProperty] private string _int32TextSwapW = string.Empty;
        [ObservableProperty] private string _float32TextSwapW = string.Empty;
        [ObservableProperty] private string _ascii4TextSwapW = string.Empty;

        [ObservableProperty] private string _raw32HexSwapBW = string.Empty;
        [ObservableProperty] private string _uint32TextSwapBW = string.Empty;
        [ObservableProperty] private string _int32TextSwapBW = string.Empty;
        [ObservableProperty] private string _float32TextSwapBW = string.Empty;
        [ObservableProperty] private string _ascii4TextSwapBW = string.Empty;

        public DecodeViewModel(
            IConnectionManager connectionManager,
            IMessageBoxService? messageBoxService = null,
            ILogger<DecodeViewModel>? logger = null)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _messageBoxService = messageBoxService;
            _logger = logger ?? NullLogger<DecodeViewModel>.Instance;

            ReadNowCommand = new AsyncRelayCommand(ReadAsync, CanRead);

            _connectionManager.ActiveProfileChanged += ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected += ConnectionManager_ProfileStateChanged;
            _connectionManager.ProfileDisconnected += ConnectionManager_ProfileStateChanged;
            SubscribeToActiveProfile(_connectionManager.ActiveProfile);
            ReadNowCommand.NotifyCanExecuteChanged();
        }

        public IReadOnlyList<string> AreaOptions { get; } = new[]
        {
            "HoldingRegister",
            "InputRegister",
            "Coil",
            "DiscreteInput"
        };

        public IReadOnlyList<int> ReadCountOptions { get; } =
            new[] { MinimumDecodeRegisters, MaxDecodeRegisters };

        public string ReadButtonText => IsBusy ? "Reading..." : "Read";

        public IAsyncRelayCommand ReadNowCommand { get; }

        public Func<byte>? UnitIdProvider { get; set; }

        private byte ActiveUnitId => UnitIdProvider?.Invoke() ?? _connectionManager.ActiveProfile?.UnitId ?? 1;

        private bool CanRead() => !IsBusy && _connectionManager.ActiveService?.IsConnected == true;

        partial void OnIsBusyChanged(bool value)
        {
            ReadNowCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ReadButtonText));
        }

        private void ConnectionManager_ActiveProfileChanged(object? sender, ConnectionProfile? profile)
        {
            SubscribeToActiveProfile(profile);
            ReadNowCommand.NotifyCanExecuteChanged();
        }

        private void ConnectionManager_ProfileStateChanged(object? sender, ConnectionProfile profile)
        {
            ReadNowCommand.NotifyCanExecuteChanged();
        }

        private void SubscribeToActiveProfile(ConnectionProfile? profile)
        {
            if (_subscribedProfile != null)
            {
                _subscribedProfile.PropertyChanged -= ActiveProfile_PropertyChanged;
            }

            _subscribedProfile = profile;

            if (_subscribedProfile != null)
            {
                _subscribedProfile.PropertyChanged += ActiveProfile_PropertyChanged;
            }
        }

        private void ActiveProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConnectionProfile.IsConnected))
            {
                ReadNowCommand.NotifyCanExecuteChanged();
            }
        }

        private async Task ReadAsync()
        {
            if (!TryParseAddress(AddressInput, out var parsedAddress))
            {
                Status = "Invalid address. Use decimal (e.g., 100) or hex (e.g., 0x64).";
                await ShowMessageAsync(Status, "Invalid address", DialogIcon.Warning);
                return;
            }

            Address = parsedAddress;

            var service = _connectionManager.ActiveService;
            if (service == null || !service.IsConnected)
            {
                Status = "Please connect to a Modbus device first.";
                await ShowMessageAsync(Status, "Not Connected", DialogIcon.Warning);
                return;
            }

            IsBusy = true;

            try
            {
                var registers = await ReadRegistersFromAreaAsync(service);

                if (registers == null || registers.Length == 0)
                {
                    Status = "No data returned";
                    return;
                }

                ProcessAndDisplayResults(registers);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                // No modal here: a read that keeps failing (device down, timeout) is
                // reported in the status bar, like the register tabs. A dialog on every
                // failed attempt would block the user while they reconnect and retry.
                _logger.LogError(ex, "Decode read failed");
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<ushort[]?> ReadRegistersFromAreaAsync(IModbusService service)
        {
            var unitId = ActiveUnitId;
            var area = Area.Trim();

            return area.ToLowerInvariant() switch
            {
                "holdingregister" => await service.ReadHoldingRegistersAsync(unitId, Address, ReadCount),
                "inputregister" => await service.ReadInputRegistersAsync(unitId, Address, ReadCount),
                "coil" => await ReadCoilsAsRegistersAsync(service, unitId),
                "discreteinput" => await ReadDiscreteInputsAsRegistersAsync(service, unitId),
                _ => throw new InvalidOperationException($"Unsupported area: {Area}")
            };
        }

        private async Task<ushort[]> ReadCoilsAsRegistersAsync(IModbusService service, byte unitId)
        {
            var coils = await service.ReadCoilsAsync(unitId, Address, ReadCount);
            return coils == null
                ? Array.Empty<ushort>()
                : coils.Select(value => (ushort)(value ? 1 : 0)).ToArray();
        }

        private async Task<ushort[]> ReadDiscreteInputsAsRegistersAsync(IModbusService service, byte unitId)
        {
            var inputs = await service.ReadDiscreteInputsAsync(unitId, Address, ReadCount);
            return inputs == null
                ? Array.Empty<ushort>()
                : inputs.Select(value => (ushort)(value ? 1 : 0)).ToArray();
        }

        private void ProcessAndDisplayResults(ushort[] registers)
        {
            var baseBytes = ConvertRegistersToBytes(registers);
            var none = ApplySwapVariant(baseBytes, swapBytes: false, swapWords: false);
            var swapB = ApplySwapVariant(baseBytes, swapBytes: true, swapWords: false);
            var swapW = ApplySwapVariant(baseBytes, swapBytes: false, swapWords: true);
            var swapBW = ApplySwapVariant(baseBytes, swapBytes: true, swapWords: true);

            Assign16BitResults(none, out var none16);
            Raw16HexNone = none16.raw16;
            Uint16TextNone = none16.u16;
            Int16TextNone = none16.i16;
            Ascii2TextNone = none16.a2;

            Assign16BitResults(swapB, out var swapB16);
            Raw16HexSwapB = swapB16.raw16;
            Uint16TextSwapB = swapB16.u16;
            Int16TextSwapB = swapB16.i16;
            Ascii2TextSwapB = swapB16.a2;

            Assign16BitResults(swapW, out var swapW16);
            Raw16HexSwapW = swapW16.raw16;
            Uint16TextSwapW = swapW16.u16;
            Int16TextSwapW = swapW16.i16;
            Ascii2TextSwapW = swapW16.a2;

            Assign16BitResults(swapBW, out var swapBW16);
            Raw16HexSwapBW = swapBW16.raw16;
            Uint16TextSwapBW = swapBW16.u16;
            Int16TextSwapBW = swapBW16.i16;
            Ascii2TextSwapBW = swapBW16.a2;

            var none32 = Compute32(none);
            Raw32HexNone = none32.raw32;
            Uint32TextNone = none32.u32;
            Int32TextNone = none32.i32;
            Float32TextNone = none32.f32;
            Ascii4TextNone = none32.a4;

            var swapB32 = Compute32(swapB);
            Raw32HexSwapB = swapB32.raw32;
            Uint32TextSwapB = swapB32.u32;
            Int32TextSwapB = swapB32.i32;
            Float32TextSwapB = swapB32.f32;
            Ascii4TextSwapB = swapB32.a4;

            var swapW32 = Compute32(swapW);
            Raw32HexSwapW = swapW32.raw32;
            Uint32TextSwapW = swapW32.u32;
            Int32TextSwapW = swapW32.i32;
            Float32TextSwapW = swapW32.f32;
            Ascii4TextSwapW = swapW32.a4;

            var swapBW32 = Compute32(swapBW);
            Raw32HexSwapBW = swapBW32.raw32;
            Uint32TextSwapBW = swapBW32.u32;
            Int32TextSwapBW = swapBW32.i32;
            Float32TextSwapBW = swapBW32.f32;
            Ascii4TextSwapBW = swapBW32.a4;

            Status = FormatSuccessMessage();
        }

        private static void Assign16BitResults(byte[] bytes, out (string raw16, string u16, string i16, string a2) result)
        {
            var value = (ushort)((bytes[0] << 8) | bytes[1]);
            var signedValue = unchecked((short)value);
            result = (
                $"0x{value:X4}",
                value.ToString(CultureInfo.InvariantCulture),
                signedValue.ToString(CultureInfo.InvariantCulture),
                BytesToAscii(bytes[0], bytes[1]));
        }

        private static (string raw32, string u32, string i32, string f32, string a4) Compute32(byte[] bytes)
        {
            var value = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            var signedValue = unchecked((int)value);
            var floatValue = BitConverter.Int32BitsToSingle(signedValue);

            return (
                $"0x{value:X8}",
                value.ToString(CultureInfo.InvariantCulture),
                signedValue.ToString(CultureInfo.InvariantCulture),
                floatValue.ToString(CultureInfo.InvariantCulture),
                BytesToAscii(bytes[0], bytes[1], bytes[2], bytes[3]));
        }

        private static byte[] ConvertRegistersToBytes(ushort[] registers)
        {
            var first = registers[0];
            var second = registers.Length > 1 ? registers[1] : (ushort)0;

            return new[]
            {
                (byte)(first >> 8), (byte)(first & 0xFF),
                (byte)(second >> 8), (byte)(second & 0xFF)
            };
        }

        private static byte[] ApplySwapVariant(byte[] input, bool swapBytes, bool swapWords)
        {
            var bytes = (byte[])input.Clone();

            if (swapBytes)
            {
                (bytes[0], bytes[1]) = (bytes[1], bytes[0]);
                (bytes[2], bytes[3]) = (bytes[3], bytes[2]);
            }

            if (swapWords)
            {
                (bytes[0], bytes[2]) = (bytes[2], bytes[0]);
                (bytes[1], bytes[3]) = (bytes[3], bytes[1]);
            }

            return bytes;
        }

        private string FormatSuccessMessage()
        {
            var areaCode = Area.Trim().ToLowerInvariant() switch
            {
                "inputregister" => "IR",
                "holdingregister" => "HR",
                "coil" => "Coil",
                "discreteinput" => "DIn",
                _ => Area
            };

            return $"Read {ReadCount} {areaCode} from {Address}";
        }

        private static string BytesToAscii(params byte[] bytes)
        {
            var cleansed = bytes
                .Select(value => value is >= 32 and <= 126 ? value : (byte)'.')
                .ToArray();
            return Encoding.ASCII.GetString(cleansed);
        }

        private async Task ShowMessageAsync(string message, string title, DialogIcon icon)
        {
            if (_messageBoxService == null)
            {
                return;
            }

            try
            {
                await _messageBoxService.ShowAsync(message, title, DialogButton.Ok, icon);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogWarning(ex, "Failed to show decode message box");
            }
        }

        private static bool TryParseAddress(string input, out int address)
        {
            address = 0;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var value = input.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address))
                {
                    return false;
                }
            }
            else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out address))
            {
                return false;
            }

            return address is >= 0 and <= MaximumAddress;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connectionManager.ActiveProfileChanged -= ConnectionManager_ActiveProfileChanged;
            _connectionManager.ProfileConnected -= ConnectionManager_ProfileStateChanged;
            _connectionManager.ProfileDisconnected -= ConnectionManager_ProfileStateChanged;
            SubscribeToActiveProfile(null);
            GC.SuppressFinalize(this);
        }
    }
}

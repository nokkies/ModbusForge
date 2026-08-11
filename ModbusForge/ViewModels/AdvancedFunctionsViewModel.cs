using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public sealed record DeviceIdentificationItem(byte ObjectId, string Name, string Value);

    public sealed class AdvancedFunctionsViewModel : ObservableObject
    {
        private const int MinimumAddress = 1;
        private const int MaximumAddress = ushort.MaxValue;
        private const int MaximumRegisterCount = 125;

        private readonly IModbusService _modbusService;
        private readonly ILogger<AdvancedFunctionsViewModel> _logger;

        private int _maskWriteAddress = MinimumAddress;
        private ushort _andMask = ushort.MaxValue;
        private ushort _orMask;
        private int _readAddress = MinimumAddress;
        private int _readCount = 4;
        private int _writeAddress = MinimumAddress;
        private string _writeValues = "0, 0";
        private DeviceIdCategory _deviceIdCategory = DeviceIdCategory.Basic;
        private string _status = string.Empty;
        private bool _isBusy;

        public AdvancedFunctionsViewModel(
            IModbusService modbusService,
            byte unitId,
            ILogger<AdvancedFunctionsViewModel> logger)
        {
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            UnitId = unitId;

            MaskWriteCommand = new AsyncRelayCommand(MaskWriteAsync, () => !IsBusy);
            ReadWriteMultipleCommand = new AsyncRelayCommand(ReadWriteMultipleAsync, () => !IsBusy);
            ReadDeviceIdentificationCommand = new AsyncRelayCommand(ReadDeviceIdentificationAsync, () => !IsBusy);
        }

        public byte UnitId { get; }

        public AsyncRelayCommand MaskWriteCommand { get; }
        public AsyncRelayCommand ReadWriteMultipleCommand { get; }
        public AsyncRelayCommand ReadDeviceIdentificationCommand { get; }

        public IReadOnlyList<DeviceIdCategory> DeviceIdCategories { get; } = new[]
        {
            DeviceIdCategory.Basic,
            DeviceIdCategory.Regular,
            DeviceIdCategory.Extended
        };

        public ObservableCollection<DeviceIdentificationItem> DeviceIdentificationItems { get; } = new();

        public int MaskWriteAddress
        {
            get => _maskWriteAddress;
            set => SetProperty(ref _maskWriteAddress, value);
        }

        public ushort AndMask
        {
            get => _andMask;
            set => SetProperty(ref _andMask, value);
        }

        public ushort OrMask
        {
            get => _orMask;
            set => SetProperty(ref _orMask, value);
        }

        public int ReadAddress
        {
            get => _readAddress;
            set => SetProperty(ref _readAddress, value);
        }

        public int ReadCount
        {
            get => _readCount;
            set => SetProperty(ref _readCount, value);
        }

        public int WriteAddress
        {
            get => _writeAddress;
            set => SetProperty(ref _writeAddress, value);
        }

        public string WriteValues
        {
            get => _writeValues;
            set => SetProperty(ref _writeValues, value);
        }

        public DeviceIdCategory DeviceIdCategory
        {
            get => _deviceIdCategory;
            set => SetProperty(ref _deviceIdCategory, value);
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    MaskWriteCommand.NotifyCanExecuteChanged();
                    ReadWriteMultipleCommand.NotifyCanExecuteChanged();
                    ReadDeviceIdentificationCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public async Task MaskWriteAsync()
        {
            if (!ValidateAddress(MaskWriteAddress, "Address"))
                return;

            await RunAsync(async () =>
            {
                var result = await _modbusService.MaskWriteRegisterAsync(UnitId, MaskWriteAddress, AndMask, OrMask);
                Status = result is null
                    ? "FC22 failed - see the console for details."
                    : $"FC22 OK - register {MaskWriteAddress} = {result} (0x{result:X4}).";
            }, "FC22 Mask Write Register failed");
        }

        public async Task ReadWriteMultipleAsync()
        {
            if (!TryParseWriteValues(WriteValues, out var values, out var parseError))
            {
                Status = parseError;
                return;
            }

            if (ReadAddress < MinimumAddress || WriteAddress < MinimumAddress)
            {
                Status = "Addresses must be 1 or greater.";
                return;
            }

            if (ReadAddress > MaximumAddress || WriteAddress > MaximumAddress)
            {
                Status = $"Addresses must be {MaximumAddress} or less.";
                return;
            }

            if (ReadCount < 1)
            {
                Status = "Read count must be 1 or greater.";
                return;
            }

            if (ReadCount > MaximumRegisterCount)
            {
                Status = $"Read count must be {MaximumRegisterCount} or less.";
                return;
            }

            if ((long)ReadAddress + ReadCount - 1 > MaximumAddress)
            {
                Status = "The read range exceeds the Modbus address space.";
                return;
            }

            if (values.Length > MaximumRegisterCount)
            {
                Status = $"Write values must contain {MaximumRegisterCount} or fewer registers.";
                return;
            }

            if ((long)WriteAddress + values.Length - 1 > MaximumAddress)
            {
                Status = "The write range exceeds the Modbus address space.";
                return;
            }

            await RunAsync(async () =>
            {
                var result = await _modbusService.ReadWriteMultipleRegistersAsync(
                    UnitId, ReadAddress, ReadCount, WriteAddress, values);
                Status = result is null
                    ? "FC23 failed - see the console for details."
                    : $"FC23 OK - read [{string.Join(", ", result)}] after writing {values.Length} register(s).";
            }, "FC23 Read/Write Multiple Registers failed");
        }

        public async Task ReadDeviceIdentificationAsync()
        {
            await RunAsync(async () =>
            {
                var identification = await _modbusService.ReadDeviceIdentificationAsync(
                    UnitId, DeviceIdObject.VendorName, DeviceIdCategory);

                DeviceIdentificationItems.Clear();
                if (identification is null)
                {
                    Status = "FC43 failed - the device did not return identification data.";
                    return;
                }

                foreach (var pair in identification.Objects.OrderBy(pair => pair.Key))
                {
                    DeviceIdentificationItems.Add(
                        new DeviceIdentificationItem(pair.Key, ObjectName(pair.Key), pair.Value));
                }

                Status = $"FC43 OK - {DeviceIdentificationItems.Count} object(s), conformity level 0x{identification.ConformityLevel:X2}.";
            }, "FC43 Read Device Identification failed");
        }

        public static bool TryParseWriteValues(string? input, out ushort[] values, out string error)
        {
            values = Array.Empty<ushort>();
            error = string.Empty;

            var tokens = (input ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                error = "Enter at least one value to write.";
                return false;
            }

            var parsed = new ushort[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i].Trim();
                var hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                var valid = hex
                    ? ushort.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i])
                    : ushort.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]);
                if (!valid)
                {
                    error = $"'{token}' is not a valid 16-bit register value.";
                    return false;
                }
            }

            values = parsed;
            return true;
        }

        private async Task RunAsync(Func<Task> operation, string errorContext)
        {
            if (!_modbusService.IsConnected)
            {
                Status = "Not connected.";
                return;
            }

            IsBusy = true;
            try
            {
                await operation();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, errorContext);
                Status = $"{errorContext}: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool ValidateAddress(int address, string label)
        {
            if (address < MinimumAddress)
            {
                Status = $"{label} must be 1 or greater.";
                return false;
            }

            if (address > MaximumAddress)
            {
                Status = $"{label} must be {MaximumAddress} or less.";
                return false;
            }

            return true;
        }

        private static string ObjectName(byte objectId) => objectId switch
        {
            DeviceIdObject.VendorName => "Vendor name",
            DeviceIdObject.ProductCode => "Product code",
            DeviceIdObject.MajorMinorRevision => "Revision",
            DeviceIdObject.VendorUrl => "Vendor URL",
            DeviceIdObject.ProductName => "Product name",
            DeviceIdObject.ModelName => "Model name",
            DeviceIdObject.UserApplicationName => "Application name",
            _ => $"Object 0x{objectId:X2}"
        };
    }
}

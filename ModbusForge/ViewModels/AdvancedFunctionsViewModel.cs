using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    /// <summary>
    /// One device identification object (FC43) as displayed in the Advanced Functions dialog.
    /// </summary>
    public record DeviceIdentificationItem(byte ObjectId, string Name, string Value);

    /// <summary>
    /// Drives the Advanced Functions dialog: FC22 Mask Write Register,
    /// FC23 Read/Write Multiple Registers and FC43 Read Device Identification.
    /// </summary>
    public class AdvancedFunctionsViewModel : ViewModelBase
    {
        private readonly IModbusService _modbusService;
        private readonly ILogger<AdvancedFunctionsViewModel> _logger;

        private int _maskWriteAddress = 1;
        private ushort _andMask = 0xFFFF;
        private ushort _orMask;
        private int _readAddress = 1;
        private int _readCount = 4;
        private int _writeAddress = 1;
        private string _writeValues = "0, 0";
        private DeviceIdCategory _deviceIdCategory = DeviceIdCategory.Basic;
        private string _status = string.Empty;
        private bool _isBusy;

        public AdvancedFunctionsViewModel(IModbusService modbusService, byte unitId, ILogger<AdvancedFunctionsViewModel> logger)
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

        /// <summary>Comma or space separated register values written by FC23.</summary>
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

        internal async Task MaskWriteAsync()
        {
            if (MaskWriteAddress < 1)
            {
                Status = "Address must be 1 or greater.";
                return;
            }

            await RunAsync(async () =>
            {
                var result = await _modbusService.MaskWriteRegisterAsync(UnitId, MaskWriteAddress, AndMask, OrMask);
                Status = result is null
                    ? "FC22 failed - see the console for details."
                    : $"FC22 OK - register {MaskWriteAddress} = {result} (0x{result:X4}).";
            }, "FC22 Mask Write Register failed");
        }

        internal async Task ReadWriteMultipleAsync()
        {
            if (!TryParseWriteValues(WriteValues, out var values, out var parseError))
            {
                Status = parseError;
                return;
            }
            if (ReadAddress < 1 || WriteAddress < 1)
            {
                Status = "Addresses must be 1 or greater.";
                return;
            }
            if (ReadCount < 1)
            {
                Status = "Read count must be 1 or greater.";
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

        internal async Task ReadDeviceIdentificationAsync()
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

                foreach (var pair in identification.Objects.OrderBy(p => p.Key))
                    DeviceIdentificationItems.Add(new DeviceIdentificationItem(pair.Key, ObjectName(pair.Key), pair.Value));

                Status = $"FC43 OK - {DeviceIdentificationItems.Count} object(s), conformity level 0x{identification.ConformityLevel:X2}.";
            }, "FC43 Read Device Identification failed");
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

        internal static bool TryParseWriteValues(string input, out ushort[] values, out string error)
        {
            values = Array.Empty<ushort>();
            error = string.Empty;

            var tokens = (input ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                error = "Enter at least one value to write.";
                return false;
            }

            var parsed = new ushort[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i].Trim();
                bool hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                bool ok = hex
                    ? ushort.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i])
                    : ushort.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]);
                if (!ok)
                {
                    error = $"'{token}' is not a valid 16-bit register value.";
                    return false;
                }
            }

            values = parsed;
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

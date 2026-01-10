using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModbusForge.ViewModels
{
    public partial class DecodeViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly ModbusTcpService _client;
        private readonly ModbusServerService _server;
        private readonly ILogger<DecodeViewModel> _logger;
        private readonly IAsyncRelayCommand _readNowCommand;

        public DecodeViewModel(MainViewModel main, ModbusTcpService client, ModbusServerService server, ILogger<DecodeViewModel> logger)
        {
            _main = main;
            _client = client;
            _server = server;
            _logger = logger;

            Area = "HoldingRegister";
            Address = 1;
            UseTwoRegisters = true; // retained for compatibility; not used by UI anymore

            _readNowCommand = new AsyncRelayCommand(ReadAsync, CanRead);
            // Ensure initial enable/disable state reflects current connection
            try { _readNowCommand.NotifyCanExecuteChanged(); } catch { }
            _main.PropertyChanged += (s, e) =>
            {
                if (string.Equals(e.PropertyName, nameof(MainViewModel.IsConnected), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(MainViewModel.Mode), StringComparison.Ordinal))
                {
                    try { _readNowCommand.NotifyCanExecuteChanged(); } catch { }
                }
            };
        }

        private IModbusService ActiveService => string.Equals(_main.Mode, "Server", StringComparison.OrdinalIgnoreCase) ? (IModbusService)_server : _client;

        [ObservableProperty]
        private string area;

        [ObservableProperty]
        private int address;

        [ObservableProperty]
        private string addressInput = "1";

        [ObservableProperty]
        private bool useTwoRegisters;

        [ObservableProperty]
        private bool swapBytes;

        [ObservableProperty]
        private bool swapWords;

        [ObservableProperty]
        private string status = string.Empty;

        [ObservableProperty]
        private bool isBusy;

        // Outputs (all swap variants)
        // 16-bit
        [ObservableProperty] private string raw16HexNone = string.Empty;
        [ObservableProperty] private string uint16TextNone = string.Empty;
        [ObservableProperty] private string int16TextNone = string.Empty;
        [ObservableProperty] private string ascii2TextNone = string.Empty;

        [ObservableProperty] private string raw16HexSwapB = string.Empty;
        [ObservableProperty] private string uint16TextSwapB = string.Empty;
        [ObservableProperty] private string int16TextSwapB = string.Empty;
        [ObservableProperty] private string ascii2TextSwapB = string.Empty;

        [ObservableProperty] private string raw16HexSwapW = string.Empty;
        [ObservableProperty] private string uint16TextSwapW = string.Empty;
        [ObservableProperty] private string int16TextSwapW = string.Empty;
        [ObservableProperty] private string ascii2TextSwapW = string.Empty;

        [ObservableProperty] private string raw16HexSwapBW = string.Empty;
        [ObservableProperty] private string uint16TextSwapBW = string.Empty;
        [ObservableProperty] private string int16TextSwapBW = string.Empty;
        [ObservableProperty] private string ascii2TextSwapBW = string.Empty;

        // 32-bit
        [ObservableProperty] private string raw32HexNone = string.Empty;
        [ObservableProperty] private string uint32TextNone = string.Empty;
        [ObservableProperty] private string int32TextNone = string.Empty;
        [ObservableProperty] private string float32TextNone = string.Empty;
        [ObservableProperty] private string ascii4TextNone = string.Empty;

        [ObservableProperty] private string raw32HexSwapB = string.Empty;
        [ObservableProperty] private string uint32TextSwapB = string.Empty;
        [ObservableProperty] private string int32TextSwapB = string.Empty;
        [ObservableProperty] private string float32TextSwapB = string.Empty;
        [ObservableProperty] private string ascii4TextSwapB = string.Empty;

        [ObservableProperty] private string raw32HexSwapW = string.Empty;
        [ObservableProperty] private string uint32TextSwapW = string.Empty;
        [ObservableProperty] private string int32TextSwapW = string.Empty;
        [ObservableProperty] private string float32TextSwapW = string.Empty;
        [ObservableProperty] private string ascii4TextSwapW = string.Empty;

        [ObservableProperty] private string raw32HexSwapBW = string.Empty;
        [ObservableProperty] private string uint32TextSwapBW = string.Empty;
        [ObservableProperty] private string int32TextSwapBW = string.Empty;
        [ObservableProperty] private string float32TextSwapBW = string.Empty;
        [ObservableProperty] private string ascii4TextSwapBW = string.Empty;

        public IAsyncRelayCommand ReadNowCommand => _readNowCommand;

        private bool CanRead()
        {
            try { return _main.IsConnected && !IsBusy; } catch { return false; }
        }

        partial void OnIsBusyChanged(bool value)
        {
            try { _readNowCommand.NotifyCanExecuteChanged(); } catch { }
        }

        private async Task ReadAsync()
        {
            try
            {
                if (!ValidateAndSetAddress())
                    return;

                IsBusy = true;
                var regs = await ReadRegistersFromAreaAsync();
                
                if (regs == null || regs.Length == 0)
                {
                    Status = "No data returned";
                    return;
                }

                ProcessAndDisplayResults(regs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Decode read failed");
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Validates address input and updates the Address property.
        /// </summary>
        /// <returns>True if valid, false otherwise</returns>
        private bool ValidateAndSetAddress()
        {
            if (!TryParseAddress(AddressInput, out var addr))
            {
                Status = "Invalid address. Use decimal (e.g., 100) or hex (e.g., 0x64).";
                return false;
            }
            Address = addr;
            return true;
        }

        /// <summary>
        /// Reads registers from the selected area type (Holding, Input, Coil, Discrete).
        /// </summary>
        /// <returns>Array of register values as ushorts</returns>
        private async Task<ushort[]?> ReadRegistersFromAreaAsync()
        {
            var svc = ActiveService;
            var unit = _main.UnitId;
            var count = 2; // Always read 2 registers for decode view

            return Area.ToLowerInvariant() switch
            {
                "holdingregister" => await svc.ReadHoldingRegistersAsync(unit, Address, count),
                "inputregister" => await svc.ReadInputRegistersAsync(unit, Address, count),
                "coil" => await ReadCoilsAsRegistersAsync(svc, unit, count),
                "discreteinput" => await ReadDiscreteInputsAsRegistersAsync(svc, unit, count),
                _ => throw new InvalidOperationException($"Unsupported area: {Area}")
            };
        }

        /// <summary>
        /// Reads coils and converts them to ushort array for uniform processing.
        /// </summary>
        private async Task<ushort[]> ReadCoilsAsRegistersAsync(IModbusService svc, byte unit, int count)
        {
            var coils = await svc.ReadCoilsAsync(unit, Address, (ushort)count);
            return coils?.Select(b => (ushort)(b ? 1 : 0)).ToArray() ?? Array.Empty<ushort>();
        }

        /// <summary>
        /// Reads discrete inputs and converts them to ushort array for uniform processing.
        /// </summary>
        private async Task<ushort[]> ReadDiscreteInputsAsRegistersAsync(IModbusService svc, byte unit, int count)
        {
            var inputs = await svc.ReadDiscreteInputsAsync(unit, Address, (ushort)count);
            return inputs?.Select(b => (ushort)(b ? 1 : 0)).ToArray() ?? Array.Empty<ushort>();
        }

        /// <summary>
        /// Processes register data and updates all display properties.
        /// </summary>
        private void ProcessAndDisplayResults(ushort[] regs)
        {
            var baseBytes = ConvertRegistersToBytes(regs);
            ComputeAndAssignVariants(baseBytes);
            Status = FormatSuccessMessage();
        }

        /// <summary>
        /// Converts register values to byte array in Modbus big-endian format.
        /// </summary>
        private static byte[] ConvertRegistersToBytes(ushort[] regs)
        {
            ushort r0 = regs[0];
            ushort r1 = regs.Length > 1 ? regs[1] : (ushort)0;
            return new byte[]
            {
                (byte)(r0 >> 8), (byte)(r0 & 0xFF),
                (byte)(r1 >> 8), (byte)(r1 & 0xFF)
            };
        }

        /// <summary>
        /// Formats success status message with area code.
        /// </summary>
        private string FormatSuccessMessage()
        {
            string areaCode = GetAreaCode();
            int count = UseTwoRegisters ? 2 : 1;
            return $"Read {count} {areaCode} from {Address}";
        }

        /// <summary>
        /// Gets the short code for the current area type.
        /// </summary>
        private string GetAreaCode()
        {
            return Area.ToLowerInvariant() switch
            {
                "inputregister" => "IR",
                "holdingregister" => "HR",
                "coil" => "Coil",
                "discreteinput" => "DIn",
                _ => Area
            };
        }

        private byte[] ApplySwap(byte[] b)
        {
            // b = [w0_hi, w0_lo, w1_hi, w1_lo]
            if (SwapBytes)
            {
                (b[0], b[1]) = (b[1], b[0]);
                (b[2], b[3]) = (b[3], b[2]);
            }
            if (SwapWords)
            {
                (b[0], b[2]) = (b[2], b[0]);
                (b[1], b[3]) = (b[3], b[1]);
            }
            return b;
        }

        private static byte[] ApplySwapVariant(byte[] input, bool swapBytes, bool swapWords)
        {
            var b = (byte[])input.Clone();
            if (swapBytes)
            {
                (b[0], b[1]) = (b[1], b[0]);
                (b[2], b[3]) = (b[3], b[2]);
            }
            if (swapWords)
            {
                (b[0], b[2]) = (b[2], b[0]);
                (b[1], b[3]) = (b[3], b[1]);
            }
            return b;
        }

        private void ComputeAndAssignVariants(byte[] baseBytes)
        {
            var none = ApplySwapVariant(baseBytes, false, false);
            var sb = ApplySwapVariant(baseBytes, true, false);
            var sw = ApplySwapVariant(baseBytes, false, true);
            var sbw = ApplySwapVariant(baseBytes, true, true);

            // 16-bit
            {
                var t = Compute16(none);    Raw16HexNone = t.raw16;   Uint16TextNone = t.u16;   Int16TextNone = t.i16;   Ascii2TextNone = t.a2;
                t = Compute16(sb);          Raw16HexSwapB = t.raw16;  Uint16TextSwapB = t.u16;  Int16TextSwapB = t.i16;  Ascii2TextSwapB = t.a2;
                t = Compute16(sw);          Raw16HexSwapW = t.raw16;  Uint16TextSwapW = t.u16;  Int16TextSwapW = t.i16;  Ascii2TextSwapW = t.a2;
                t = Compute16(sbw);         Raw16HexSwapBW = t.raw16; Uint16TextSwapBW = t.u16; Int16TextSwapBW = t.i16; Ascii2TextSwapBW = t.a2;
            }

            // 32-bit
            {
                var t2 = Compute32(none);   Raw32HexNone = t2.raw32;   Uint32TextNone = t2.u32;   Int32TextNone = t2.i32;   Float32TextNone = t2.f32;   Ascii4TextNone = t2.a4;
                t2 = Compute32(sb);         Raw32HexSwapB = t2.raw32;  Uint32TextSwapB = t2.u32;  Int32TextSwapB = t2.i32;  Float32TextSwapB = t2.f32;  Ascii4TextSwapB = t2.a4;
                t2 = Compute32(sw);         Raw32HexSwapW = t2.raw32;  Uint32TextSwapW = t2.u32;  Int32TextSwapW = t2.i32;  Float32TextSwapW = t2.f32;  Ascii4TextSwapW = t2.a4;
                t2 = Compute32(sbw);        Raw32HexSwapBW = t2.raw32; Uint32TextSwapBW = t2.u32; Int32TextSwapBW = t2.i32; Float32TextSwapBW = t2.f32; Ascii4TextSwapBW = t2.a4;
            }
        }

        private static (string raw16, string u16, string i16, string a2) Compute16(byte[] b)
        {
            ushort val = (ushort)((b[0] << 8) | b[1]);
            short sval = unchecked((short)val);
            var raw16 = $"0x{val:X4}";
            var u16 = val.ToString(CultureInfo.InvariantCulture);
            var i16 = sval.ToString(CultureInfo.InvariantCulture);
            var a2 = BytesToAscii(b[0], b[1]);
            return (raw16, u16, i16, a2);
        }

        private static (string raw32, string u32, string i32, string f32, string a4) Compute32(byte[] b)
        {
            uint val = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
            int ival = unchecked((int)val);
            float fval = BitConverter.Int32BitsToSingle(ival);
            var raw32 = $"0x{val:X8}";
            var u32 = val.ToString(CultureInfo.InvariantCulture);
            var i32 = ival.ToString(CultureInfo.InvariantCulture);
            var f32 = fval.ToString(CultureInfo.InvariantCulture);
            var a4 = BytesToAscii(b[0], b[1], b[2], b[3]);
            return (raw32, u32, i32, f32, a4);
        }

        private static string BytesToAscii(params byte[] bytes)
        {
            try
            {
                var cleansed = bytes.Select(ch => ch >= 32 && ch <= 126 ? ch : (byte)46).ToArray();
                return Encoding.ASCII.GetString(cleansed);
            }
            catch { return string.Empty; }
        }

        private static bool TryParseAddress(string input, out int addr)
        {
            addr = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            var s = input.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr)) return false;
            }
            else if (s.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr)) return false;
            }
            else
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out addr)) return false;
            }
            return addr >= 0 && addr <= 0xFFFF;
        }
    }
}

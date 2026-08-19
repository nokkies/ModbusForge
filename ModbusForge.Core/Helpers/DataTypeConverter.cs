using System;
using System.Linq;
using System.Text;
using ModbusForge.Models;

namespace ModbusForge.Helpers
{
    public static class DataTypeConverter
    {
        /// <summary>
        /// Number of 16-bit registers a tag data type occupies.
        /// </summary>
        public static int GetRegisterCount(TagDataType dataType)
        {
            return dataType switch
            {
                TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float or TagDataType.String => 2,
                TagDataType.Double => 4,
                _ => 1
            };
        }

        /// <summary>
        /// Converts a freshly read register payload to the tag's data type.
        /// Registers are in Modbus wire order (big-endian words); callers must
        /// pass exactly <see cref="GetRegisterCount"/> registers.
        /// This is the single place where register payloads become typed values,
        /// shared by the watch window and the tag store.
        /// </summary>
        public static object ConvertRegisters(TagDataType dataType, ushort[] registers)
        {
            if (registers is null) throw new ArgumentNullException(nameof(registers));

            switch (dataType)
            {
                case TagDataType.Int16:
                    return unchecked((short)registers[0]);
                case TagDataType.UInt16:
                    return registers[0];
                case TagDataType.Bool:
                    return registers[0] != 0;
                case TagDataType.Int32:
                    return ToInt32(RegistersToBytes(registers), EndiannessFormat.ABCD_BigEndian);
                case TagDataType.UInt32:
                    return ToUInt32(RegistersToBytes(registers), EndiannessFormat.ABCD_BigEndian);
                case TagDataType.Float:
                    return ToFloat32(RegistersToBytes(registers), EndiannessFormat.ABCD_BigEndian);
                case TagDataType.Double:
                    return ToFloat64(RegistersToBytes(registers), EndiannessFormat.ABCD_BigEndian);
                case TagDataType.String:
                    {
                        var bytes = RegistersToBytes(registers);
                        var end = Array.IndexOf(bytes, (byte)0);
                        if (end >= 0)
                            Array.Resize(ref bytes, end);
                        return Encoding.ASCII.GetString(bytes);
                    }
                default:
                    return registers[0];
            }
        }

        /// <summary>
        /// Register payload to big-endian byte order (the Modbus wire layout),
        /// matching what <see cref="DataTypeConverter"/> expects.
        /// </summary>
        public static byte[] RegistersToBytes(ushort[] registers)
        {
            var bytes = new byte[registers.Length * 2];
            for (var i = 0; i < registers.Length; i++)
            {
                bytes[i * 2] = (byte)(registers[i] >> 8);
                bytes[i * 2 + 1] = (byte)(registers[i] & 0xFF);
            }

            return bytes;
        }

        /// <summary>
        /// Maps the legacy pair of boolean swap flags to an <see cref="EndiannessFormat"/>.
        /// </summary>
        public static EndiannessFormat GetEndianness(bool swapBytes, bool swapWords) => (swapBytes, swapWords) switch
        {
            (false, false) => EndiannessFormat.ABCD_BigEndian,
            (true, false) => EndiannessFormat.BADC_ByteSwap,
            (false, true) => EndiannessFormat.CDAB_WordSwap,
            (true, true) => EndiannessFormat.DCBA_LittleEndian
        };

        public static float ToSingle(ushort high, ushort low, bool swapBytes = false, bool swapWords = false)
        {
            var format = GetEndianness(swapBytes, swapWords);
            var bytes = new byte[] { (byte)(high >> 8), (byte)(high & 0xFF), (byte)(low >> 8), (byte)(low & 0xFF) };
            return ToFloat32(bytes, format);
        }

        public static ushort[] ToUInt16(float value, bool swapBytes = false, bool swapWords = false)
        {
            var format = GetEndianness(swapBytes, swapWords);
            var bytes = GetBytes(value, format);
            return new ushort[]
            {
                (ushort)((bytes[0] << 8) | bytes[1]),
                (ushort)((bytes[2] << 8) | bytes[3])
            };
        }

        public static string ToString(ushort value)
        {
            char c1 = (char)(value >> 8);
            char c2 = (char)(value & 0xFF);
            return new string(new[] { c1, c2 }).TrimEnd('\0');
        }

        public static ushort[] ToUInt16(string text)
        {
            text ??= string.Empty;
            var bytes = Encoding.ASCII.GetBytes(text);
            if ((bytes.Length & 1) != 0)
            {
                Array.Resize(ref bytes, bytes.Length + 1);
                bytes[^1] = 0;
            }

            var result = new ushort[bytes.Length / 2];
            for (int i = 0; i < bytes.Length; i += 2)
            {
                result[i / 2] = (ushort)((bytes[i] << 8) | bytes[i + 1]);
            }
            return result;
        }

        /// <summary>
        /// Reorders a byte array so it can be passed to the little-endian <see cref="BitConverter"/>.
        /// </summary>
        private static byte[] ToLittleEndianBytes(byte[] bytes, EndiannessFormat format)
            => ReorderBytes(bytes, format, toLittleEndian: true);

        private static byte[] FromLittleEndianBytes(byte[] littleEndianBytes, EndiannessFormat format)
            => ReorderBytes(littleEndianBytes, format, toLittleEndian: false);

        private static byte[] ReorderBytes(byte[] bytes, EndiannessFormat format, bool toLittleEndian)
        {
            if (bytes is null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length != 4 && bytes.Length != 8)
                throw new ArgumentException("Endianness conversion requires 4 or 8 bytes.", nameof(bytes));

            int[] indices = (bytes.Length, format) switch
            {
                (4, EndiannessFormat.ABCD_BigEndian) => new[] { 3, 2, 1, 0 },
                (4, EndiannessFormat.DCBA_LittleEndian) => new[] { 0, 1, 2, 3 },
                (4, EndiannessFormat.CDAB_WordSwap) => new[] { 1, 0, 3, 2 },
                (4, EndiannessFormat.BADC_ByteSwap) => new[] { 2, 3, 0, 1 },

                (8, EndiannessFormat.ABCD_BigEndian) => new[] { 7, 6, 5, 4, 3, 2, 1, 0 },
                (8, EndiannessFormat.DCBA_LittleEndian) => new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                (8, EndiannessFormat.CDAB_WordSwap) => new[] { 5, 4, 7, 6, 1, 0, 3, 2 },
                (8, EndiannessFormat.BADC_ByteSwap) => new[] { 6, 7, 4, 5, 2, 3, 0, 1 },

                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            return indices.Select(i => bytes[i]).ToArray();
        }

        public static float ToFloat32(byte[] bytes, EndiannessFormat format)
            => BitConverter.ToSingle(ToLittleEndianBytes(bytes, format), 0);

        public static double ToFloat64(byte[] bytes, EndiannessFormat format)
            => BitConverter.ToDouble(ToLittleEndianBytes(bytes, format), 0);

        public static int ToInt32(byte[] bytes, EndiannessFormat format)
            => BitConverter.ToInt32(ToLittleEndianBytes(bytes, format), 0);

        public static uint ToUInt32(byte[] bytes, EndiannessFormat format)
            => BitConverter.ToUInt32(ToLittleEndianBytes(bytes, format), 0);

        public static long ToInt64(byte[] bytes, EndiannessFormat format)
            => BitConverter.ToInt64(ToLittleEndianBytes(bytes, format), 0);

        public static ulong ToUInt64(byte[] bytes, EndiannessFormat format)
            => BitConverter.ToUInt64(ToLittleEndianBytes(bytes, format), 0);

        public static byte[] GetBytes(float value, EndiannessFormat format)
            => FromLittleEndianBytes(BitConverter.GetBytes(value), format);

        public static byte[] GetBytes(double value, EndiannessFormat format)
            => FromLittleEndianBytes(BitConverter.GetBytes(value), format);

        public static byte[] GetBytes(int value, EndiannessFormat format)
            => FromLittleEndianBytes(BitConverter.GetBytes(value), format);

        public static byte[] GetBytes(uint value, EndiannessFormat format)
            => FromLittleEndianBytes(BitConverter.GetBytes(value), format);

        public static byte[] GetBytes(long value, EndiannessFormat format)
            => FromLittleEndianBytes(BitConverter.GetBytes(value), format);

        public static byte[] GetBytes(ulong value, EndiannessFormat format)
            => FromLittleEndianBytes(BitConverter.GetBytes(value), format);
    }
}

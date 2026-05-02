using System;
using System.Text;

namespace ModbusForge.Helpers
{
    public static class DataTypeConverter
    {
        public static float ToSingle(ushort high, ushort low)
        {
            byte[] b = new byte[4]
            {
                (byte)(high >> 8), (byte)(high & 0xFF),
                (byte)(low >> 8),  (byte)(low & 0xFF)
            };
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        public static ushort[] ToUInt16(float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            ushort high = (ushort)((bytes[0] << 8) | bytes[1]);
            ushort low = (ushort)((bytes[2] << 8) | bytes[3]);

            return new ushort[] { high, low };
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
    }
}

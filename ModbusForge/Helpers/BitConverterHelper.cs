using System;

namespace ModbusForge.Helpers
{
    public static class BitConverterHelper
    {
        public static bool[] ToBooleanArray(ReadOnlySpan<byte> bytes, int count)
        {
            var result = new bool[count];
            int bitIndex = 0;
            for (int i = 0; i < bytes.Length && bitIndex < count; i++)
            {
                byte b = bytes[i];
                for (int bit = 0; bit < 8 && bitIndex < count; bit++)
                {
                    result[bitIndex++] = (b & (1 << bit)) != 0;
                }
            }
            return result;
        }
    }
}

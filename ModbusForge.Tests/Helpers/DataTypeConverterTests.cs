using System;
using ModbusForge.Helpers;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class DataTypeConverterTests
    {
        [Theory]
        [InlineData(1.0f)]
        [InlineData(0.0f)]
        [InlineData(-1.0f)]
        [InlineData(123.456f)]
        [InlineData(-987.654f)]
        [InlineData(float.MaxValue)]
        [InlineData(float.MinValue)]
        [InlineData(float.Epsilon)]
        public void FloatConversion_RoundTrip_ReturnsOriginalValue(float value)
        {
            // Act
            ushort[] registers = DataTypeConverter.ToUInt16(value);
            float result = DataTypeConverter.ToSingle(registers[0], registers[1]);

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public void ToSingle_NaN_ReturnsNaN()
        {
            // Arrange
            ushort[] registers = DataTypeConverter.ToUInt16(float.NaN);

            // Act
            float result = DataTypeConverter.ToSingle(registers[0], registers[1]);

            // Assert
            Assert.True(float.IsNaN(result));
        }

        [Theory]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void ToSingle_Infinity_ReturnsInfinity(float infinity)
        {
            // Act
            ushort[] registers = DataTypeConverter.ToUInt16(infinity);
            float result = DataTypeConverter.ToSingle(registers[0], registers[1]);

            // Assert
            Assert.Equal(infinity, result);
        }

        [Theory]
        [InlineData("AB", new ushort[] { 0x4142 })] // 'A'=0x41, 'B'=0x42
        [InlineData("A", new ushort[] { 0x4100 })]  // 'A'=0x41, '\0'=0x00
        [InlineData("", new ushort[] { })]
        [InlineData("ABCD", new ushort[] { 0x4142, 0x4344 })]
        public void ToUInt16_String_ReturnsExpectedRegisters(string input, ushort[] expected)
        {
            // Act
            ushort[] result = DataTypeConverter.ToUInt16(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x4142, "AB")]
        [InlineData(0x4100, "A")]
        [InlineData(0x0000, "")]
        public void ToString_UInt16_ReturnsExpectedString(ushort input, string expected)
        {
            // Act
            string result = DataTypeConverter.ToString(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToUInt16_NullString_ReturnsEmptyArray()
        {
            // Act
            ushort[] result = DataTypeConverter.ToUInt16(null!);

            // Assert
            Assert.Empty(result);
        }

        [Theory]
        [InlineData(1.0f, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(0.0f, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(-1.0f, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(123456.0f, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(1.0f, EndiannessFormat.BADC_ByteSwap)]
        [InlineData(123456.0f, EndiannessFormat.BADC_ByteSwap)]
        [InlineData(1.0f, EndiannessFormat.CDAB_WordSwap)]
        [InlineData(123456.0f, EndiannessFormat.CDAB_WordSwap)]
        [InlineData(1.0f, EndiannessFormat.DCBA_LittleEndian)]
        [InlineData(123456.0f, EndiannessFormat.DCBA_LittleEndian)]
        public void Float32_RoundTrip_AllFormats(float value, EndiannessFormat format)
        {
            byte[] bytes = DataTypeConverter.GetBytes(value, format);
            float result = DataTypeConverter.ToFloat32(bytes, format);
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(123456.0, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(-987654.0, EndiannessFormat.BADC_ByteSwap)]
        [InlineData(3.14159265358979, EndiannessFormat.CDAB_WordSwap)]
        [InlineData(-2.71828182845904, EndiannessFormat.DCBA_LittleEndian)]
        public void Float64_RoundTrip_AllFormats(double value, EndiannessFormat format)
        {
            byte[] bytes = DataTypeConverter.GetBytes(value, format);
            double result = DataTypeConverter.ToFloat64(bytes, format);
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(0x12345678, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(-0x12345678, EndiannessFormat.BADC_ByteSwap)]
        [InlineData(0x12345678, EndiannessFormat.CDAB_WordSwap)]
        [InlineData(-0x12345678, EndiannessFormat.DCBA_LittleEndian)]
        public void Int32_RoundTrip_AllFormats(int value, EndiannessFormat format)
        {
            byte[] bytes = DataTypeConverter.GetBytes(value, format);
            int result = DataTypeConverter.ToInt32(bytes, format);
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(0x123456789ABCDEF0, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(0x123456789ABCDEF0, EndiannessFormat.DCBA_LittleEndian)]
        [InlineData(0x123456789ABCDEF0, EndiannessFormat.CDAB_WordSwap)]
        [InlineData(0x123456789ABCDEF0, EndiannessFormat.BADC_ByteSwap)]
        public void Int64_RoundTrip_AllFormats(long value, EndiannessFormat format)
        {
            byte[] bytes = DataTypeConverter.GetBytes(value, format);
            long result = DataTypeConverter.ToInt64(bytes, format);
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(123456.0f, false, false, EndiannessFormat.ABCD_BigEndian)]
        [InlineData(123456.0f, true, false, EndiannessFormat.BADC_ByteSwap)]
        [InlineData(123456.0f, false, true, EndiannessFormat.CDAB_WordSwap)]
        [InlineData(123456.0f, true, true, EndiannessFormat.DCBA_LittleEndian)]
        public void LegacySwapFlags_MatchEndiannessFormat(float value, bool swapBytes, bool swapWords, EndiannessFormat format)
        {
            ushort[] legacy = DataTypeConverter.ToUInt16(value, swapBytes, swapWords);
            byte[] bytes = DataTypeConverter.GetBytes(value, format);

            Assert.Equal(legacy, new[] { (ushort)((bytes[0] << 8) | bytes[1]), (ushort)((bytes[2] << 8) | bytes[3]) });
            Assert.Equal(value, DataTypeConverter.ToSingle(legacy[0], legacy[1], swapBytes, swapWords));
        }
    }
}

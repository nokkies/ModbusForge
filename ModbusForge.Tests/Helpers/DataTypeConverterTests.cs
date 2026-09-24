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

        [Fact]
        public void RegistersToBytes_NullRegisters_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DataTypeConverter.RegistersToBytes(null!));
        }

        [Fact]
        public void RegistersToBytes_EmptyRegisters_ReturnsEmptyByteArray()
        {
            byte[] result = DataTypeConverter.RegistersToBytes(Array.Empty<ushort>());
            Assert.Empty(result);
        }

        [Theory]
        [InlineData((ushort)0x1234, new byte[] { 0x12, 0x34 })]
        [InlineData((ushort)0x00FF, new byte[] { 0x00, 0xFF })]
        [InlineData((ushort)0xFF00, new byte[] { 0xFF, 0x00 })]
        [InlineData((ushort)0x0000, new byte[] { 0x00, 0x00 })]
        [InlineData((ushort)0xFFFF, new byte[] { 0xFF, 0xFF })]
        public void RegistersToBytes_SingleRegister_ReturnsBigEndianBytes(ushort input, byte[] expected)
        {
            byte[] result = DataTypeConverter.RegistersToBytes(new[] { input });
            Assert.Equal(expected, result);
        }

        [Fact]
        public void RegistersToBytes_MultipleRegisters_ReturnsExpectedBigEndianBytes()
        {
            ushort[] registers = new ushort[] { 0x1234, 0x5678, 0x9ABC };
            byte[] expected = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC };

            byte[] result = DataTypeConverter.RegistersToBytes(registers);

            Assert.Equal(6, result.Length);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertRegisters_NullRegisters_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DataTypeConverter.ConvertRegisters(TagDataType.Int16, null!));
        }

        [Fact]
        public void ConvertRegisters_Int16_ReturnsShort()
        {
            ushort[] registers = new ushort[] { 0xFF00 }; // -256 as short
            object result = DataTypeConverter.ConvertRegisters(TagDataType.Int16, registers);
            Assert.Equal((short)-256, result);
        }

        [Fact]
        public void ConvertRegisters_UInt16_ReturnsUShort()
        {
            ushort[] registers = new ushort[] { 0x1234 };
            object result = DataTypeConverter.ConvertRegisters(TagDataType.UInt16, registers);
            Assert.Equal((ushort)0x1234, result);
        }

        [Fact]
        public void ConvertRegisters_Bool_ReturnsExpectedBoolean()
        {
            Assert.True((bool)DataTypeConverter.ConvertRegisters(TagDataType.Bool, new ushort[] { 1 }));
            Assert.False((bool)DataTypeConverter.ConvertRegisters(TagDataType.Bool, new ushort[] { 0 }));
        }

        [Fact]
        public void ConvertRegisters_Int32_ReturnsInteger()
        {
            // 0x12345678 in big-endian high word=0x1234, low word=0x5678
            ushort[] registers = new ushort[] { 0x1234, 0x5678 };
            object result = DataTypeConverter.ConvertRegisters(TagDataType.Int32, registers);
            Assert.Equal(0x12345678, result);
        }

        [Fact]
        public void ConvertRegisters_UInt32_ReturnsUInteger()
        {
            ushort[] registers = new ushort[] { 0x8234, 0x5678 };
            object result = DataTypeConverter.ConvertRegisters(TagDataType.UInt32, registers);
            Assert.Equal(0x82345678u, result);
        }

        [Fact]
        public void ConvertRegisters_Float_ReturnsSingle()
        {
            // 1.0f in IEEE 754 Big-Endian bytes: 0x3F, 0x80, 0x00, 0x00 -> registers 0x3F80, 0x0000
            ushort[] registers = new ushort[] { 0x3F80, 0x0000 };
            object result = DataTypeConverter.ConvertRegisters(TagDataType.Float, registers);
            Assert.Equal(1.0f, result);
        }

        [Fact]
        public void ConvertRegisters_Double_ReturnsDouble()
        {
            // 1.0 in IEEE 754 Big-Endian: 0x3FF0000000000000 -> registers 0x3FF0, 0x0000, 0x0000, 0x0000
            ushort[] registers = new ushort[] { 0x3FF0, 0x0000, 0x0000, 0x0000 };
            object result = DataTypeConverter.ConvertRegisters(TagDataType.Double, registers);
            Assert.Equal(1.0, result);
        }

        [Fact]
        public void ConvertRegisters_String_ReturnsNullTerminatedAsciiString()
        {
            // "Test" -> 'T'=0x54, 'e'=0x65, 's'=0x73, 't'=0x74, then null terminator
            ushort[] registers = new ushort[] { 0x5465, 0x7374, 0x0000 };
            object result = DataTypeConverter.ConvertRegisters(TagDataType.String, registers);
            Assert.Equal("Test", result);
        }
    }
}

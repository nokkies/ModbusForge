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
        public void ConvertRegisters_NullRegisters_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DataTypeConverter.ConvertRegisters(TagDataType.Int16, null!));
        }

        [Theory]
        [InlineData(0x0000, false)]
        [InlineData(0x0001, true)]
        [InlineData(0x1234, true)]
        public void ConvertRegisters_Bool_ReturnsExpectedBoolean(ushort register, bool expected)
        {
            var result = DataTypeConverter.ConvertRegisters(TagDataType.Bool, new ushort[] { register });
            Assert.IsType<bool>(result);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x0000, (short)0)]
        [InlineData(0x0064, (short)100)]
        [InlineData(0xFF9C, (short)-100)]
        [InlineData(0x7FFF, short.MaxValue)]
        [InlineData(0x8000, short.MinValue)]
        public void ConvertRegisters_Int16_ReturnsExpectedShort(ushort register, short expected)
        {
            var result = DataTypeConverter.ConvertRegisters(TagDataType.Int16, new ushort[] { register });
            Assert.IsType<short>(result);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0x0000, (ushort)0)]
        [InlineData(0x0064, (ushort)100)]
        [InlineData(0xFFFF, ushort.MaxValue)]
        public void ConvertRegisters_UInt16_ReturnsExpectedUShort(ushort register, ushort expected)
        {
            var result = DataTypeConverter.ConvertRegisters(TagDataType.UInt16, new ushort[] { register });
            Assert.IsType<ushort>(result);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(new ushort[] { 0x0000, 0x0064 }, 100)]
        [InlineData(new ushort[] { 0x1234, 0x5678 }, 0x12345678)]
        [InlineData(new ushort[] { 0xFFFF, 0xFF9C }, -100)]
        [InlineData(new ushort[] { 0x7FFF, 0xFFFF }, int.MaxValue)]
        [InlineData(new ushort[] { 0x8000, 0x0000 }, int.MinValue)]
        public void ConvertRegisters_Int32_ReturnsExpectedInt(ushort[] registers, int expected)
        {
            var result = DataTypeConverter.ConvertRegisters(TagDataType.Int32, registers);
            Assert.IsType<int>(result);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(new ushort[] { 0x0000, 0x0064 }, 100u)]
        [InlineData(new ushort[] { 0x1234, 0x5678 }, 0x12345678u)]
        [InlineData(new ushort[] { 0xFFFF, 0xFFFF }, uint.MaxValue)]
        public void ConvertRegisters_UInt32_ReturnsExpectedUInt(ushort[] registers, uint expected)
        {
            var result = DataTypeConverter.ConvertRegisters(TagDataType.UInt32, registers);
            Assert.IsType<uint>(result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertRegisters_Float_ReturnsExpectedFloat()
        {
            var registers = new ushort[] { 0x42C8, 0x0000 };
            var result = DataTypeConverter.ConvertRegisters(TagDataType.Float, registers);
            Assert.IsType<float>(result);
            Assert.Equal(100.0f, result);
        }

        [Fact]
        public void ConvertRegisters_Double_ReturnsExpectedDouble()
        {
            var registers = new ushort[] { 0x4059, 0x0000, 0x0000, 0x0000 };
            var result = DataTypeConverter.ConvertRegisters(TagDataType.Double, registers);
            Assert.IsType<double>(result);
            Assert.Equal(100.0, result);
        }

        [Theory]
        [InlineData(new ushort[] { 0x4865, 0x6C6C, 0x6F00 }, "Hello")]
        [InlineData(new ushort[] { 0x5465, 0x7374 }, "Test")]
        [InlineData(new ushort[] { 0x0041, 0x4243 }, "")]
        public void ConvertRegisters_String_ReturnsExpectedString(ushort[] registers, string expected)
        {
            var result = DataTypeConverter.ConvertRegisters(TagDataType.String, registers);
            Assert.IsType<string>(result);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertRegisters_UndefinedDataType_ReturnsFirstRegisterAsUShort()
        {
            var unmappedType = (TagDataType)999;
            var registers = new ushort[] { 0x1234, 0x5678 };
            var result = DataTypeConverter.ConvertRegisters(unmappedType, registers);
            Assert.IsType<ushort>(result);
            Assert.Equal((ushort)0x1234, result);
        }

        [Theory]
        [InlineData(TagDataType.Bool, 1)]
        [InlineData(TagDataType.Int16, 1)]
        [InlineData(TagDataType.UInt16, 1)]
        [InlineData(TagDataType.Int32, 2)]
        [InlineData(TagDataType.UInt32, 2)]
        [InlineData(TagDataType.Float, 2)]
        [InlineData(TagDataType.Double, 4)]
        [InlineData(TagDataType.String, 2)]
        public void GetRegisterCount_AllDataTypes_ReturnsExpectedCount(TagDataType dataType, int expectedCount)
        {
            int count = DataTypeConverter.GetRegisterCount(dataType);
            Assert.Equal(expectedCount, count);
        }
    }
}

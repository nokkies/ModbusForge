using System;
using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class DataTypeConverterTests
    {
        [Theory]
        [InlineData(0x3F80, 0x0000, 1.0f)]
        [InlineData(0xBF80, 0x0000, -1.0f)]
        [InlineData(0x0000, 0x0000, 0.0f)]
        [InlineData(0x42F6, 0xE979, 123.456f)] // ~123.456
        public void ToSingle_ExplicitRegisters_ReturnsExpectedFloat(ushort high, ushort low, float expected)
        {
            // Act
            float result = DataTypeConverter.ToSingle(high, low);

            // Assert
            // Use precision 3 for 123.456f test as floats are imprecise
            Assert.Equal(expected, result, 3);
        }

        [Theory]
        [InlineData(1.0f, new ushort[] { 0x3F80, 0x0000 })]
        [InlineData(-1.0f, new ushort[] { 0xBF80, 0x0000 })]
        [InlineData(0.0f, new ushort[] { 0x0000, 0x0000 })]
        [InlineData(123.456f, new ushort[] { 0x42F6, 0xE979 })]
        public void ToUInt16_ExplicitFloat_ReturnsExpectedRegisters(float input, ushort[] expected)
        {
            // Act
            ushort[] result = DataTypeConverter.ToUInt16(input);

            // Assert
            Assert.Equal(expected, result);
        }

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
    }
}

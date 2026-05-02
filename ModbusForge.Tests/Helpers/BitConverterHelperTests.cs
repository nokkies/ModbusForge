using System;
using Xunit;
using ModbusForge.Helpers;

namespace ModbusForge.Tests.Helpers
{
    public class BitConverterHelperTests
    {
        [Fact]
        public void ToBooleanArray_ExtractsLsbFirst()
        {
            // Arrange
            byte[] bytes = { 0b00000001 }; // Only LSB is set

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, 8);

            // Assert
            Assert.Equal(8, result.Length);
            Assert.True(result[0]);
            for (int i = 1; i < 8; i++)
            {
                Assert.False(result[i]);
            }
        }

        [Fact]
        public void ToBooleanArray_ExtractsMultiByte()
        {
            // Arrange
            // 0xAA = 10101010 (LSB first: 0,1,0,1,0,1,0,1)
            // 0x55 = 01010101 (LSB first: 1,0,1,0,1,0,1,0)
            byte[] bytes = { 0xAA, 0x55 };

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, 16);

            // Assert
            Assert.Equal(16, result.Length);
            bool[] expected = {
                false, true, false, true, false, true, false, true,
                true, false, true, false, true, false, true, false
            };
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ToBooleanArray_TruncatesWhenCountIsLess()
        {
            // Arrange
            byte[] bytes = { 0b11111111, 0b11111111 };

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, 3);

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal(new[] { true, true, true }, result);
        }

        [Fact]
        public void ToBooleanArray_PadsWithFalseWhenCountIsMore()
        {
            // Arrange
            byte[] bytes = { 0b00000001 };

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, 10);

            // Assert
            Assert.Equal(10, result.Length);
            Assert.True(result[0]);
            for (int i = 1; i < 10; i++)
            {
                Assert.False(result[i]);
            }
        }

        [Fact]
        public void ToBooleanArray_EmptyBytes()
        {
            // Arrange
            byte[] bytes = Array.Empty<byte>();

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, 5);

            // Assert
            Assert.Equal(5, result.Length);
            Assert.Equal(new[] { false, false, false, false, false }, result);
        }

        [Fact]
        public void ToBooleanArray_ZeroCount()
        {
            // Arrange
            byte[] bytes = { 0b11111111 };

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, 0);

            // Assert
            Assert.Empty(result);
        }
    }
}

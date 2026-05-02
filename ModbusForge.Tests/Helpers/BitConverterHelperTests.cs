using System;
using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class BitConverterHelperTests
    {
        [Fact]
        public void ToBooleanArray_SingleByte_ReturnsExpectedBits()
        {
            // Arrange
            byte[] bytes = { 0b00000001 }; // Bit 0 is set
            int count = 8;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(8, result.Length);
            Assert.True(result[0]);
            for (int i = 1; i < 8; i++)
            {
                Assert.False(result[i]);
            }
        }

        [Fact]
        public void ToBooleanArray_MultipleBytes_ReturnsExpectedBits()
        {
            // Arrange
            byte[] bytes = { 0b00000001, 0b00000010 }; // Bit 0 in first byte, Bit 1 in second byte
            int count = 16;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(16, result.Length);
            Assert.True(result[0]); // Bit 0
            Assert.False(result[1]);
            Assert.True(result[9]); // Bit 1 of second byte (8 + 1)
            Assert.False(result[8]);
        }

        [Fact]
        public void ToBooleanArray_Truncation_ReturnsOnlyRequestedCount()
        {
            // Arrange
            byte[] bytes = { 0xFF }; // All bits set
            int count = 4;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(4, result.Length);
            foreach (var bit in result)
            {
                Assert.True(bit);
            }
        }

        [Fact]
        public void ToBooleanArray_Padding_ReturnsFalseForExtraBits()
        {
            // Arrange
            byte[] bytes = { 0xFF }; // All bits set in the only byte
            int count = 12;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(12, result.Length);
            for (int i = 0; i < 8; i++)
            {
                Assert.True(result[i]);
            }
            for (int i = 8; i < 12; i++)
            {
                Assert.False(result[i]);
            }
        }

        [Fact]
        public void ToBooleanArray_EmptyInput_ReturnsAllFalse()
        {
            // Arrange
            byte[] bytes = Array.Empty<byte>();
            int count = 4;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(4, result.Length);
            foreach (var bit in result)
            {
                Assert.False(bit);
            }
        }

        [Fact]
        public void ToBooleanArray_ZeroCount_ReturnsEmptyArray()
        {
            // Arrange
            byte[] bytes = { 0xFF };
            int count = 0;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Empty(result);
        }

        [Theory]
        [InlineData(0xAA, new[] { false, true, false, true, false, true, false, true })] // 10101010
        [InlineData(0x55, new[] { true, false, true, false, true, false, true, false })] // 01010101
        public void ToBooleanArray_SpecificPatterns_ReturnsExpected(byte input, bool[] expected)
        {
            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(new[] { input }, 8);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}

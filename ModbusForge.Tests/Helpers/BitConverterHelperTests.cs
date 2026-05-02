using System;
using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class BitConverterHelperTests
    {
        [Fact]
        public void ToBooleanArray_SingleByte_ReturnsCorrectBits()
        {
            // Arrange
            byte[] bytes = { 0b10101010 }; // 0xAA, decimal 170
            // Bits (LSB first): 0, 1, 0, 1, 0, 1, 0, 1
            int count = 8;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(count, result.Length);
            Assert.False(result[0]);
            Assert.True(result[1]);
            Assert.False(result[2]);
            Assert.True(result[3]);
            Assert.False(result[4]);
            Assert.True(result[5]);
            Assert.False(result[6]);
            Assert.True(result[7]);
        }

        [Fact]
        public void ToBooleanArray_MultipleBytes_ReturnsCorrectBits()
        {
            // Arrange
            byte[] bytes = { 0x01, 0x80 };
            // 0x01: 1, 0, 0, 0, 0, 0, 0, 0
            // 0x80: 0, 0, 0, 0, 0, 0, 0, 1
            int count = 16;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(count, result.Length);
            Assert.True(result[0]);
            Assert.False(result[1]);
            Assert.False(result[14]);
            Assert.True(result[15]);
        }

        [Fact]
        public void ToBooleanArray_Truncation_ReturnsOnlyRequestedCount()
        {
            // Arrange
            byte[] bytes = { 0xFF }; // All true
            int count = 3;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(count, result.Length);
            Assert.All(result, Assert.True);
        }

        [Fact]
        public void ToBooleanArray_Padding_FillsWithFalse()
        {
            // Arrange
            byte[] bytes = { 0xFF };
            int count = 10;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(count, result.Length);
            for (int i = 0; i < 8; i++) Assert.True(result[i]);
            for (int i = 8; i < 10; i++) Assert.False(result[i]);
        }

        [Fact]
        public void ToBooleanArray_EmptyInput_ReturnsFalseArray()
        {
            // Arrange
            byte[] bytes = Array.Empty<byte>();
            int count = 5;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(count, result.Length);
            Assert.All(result, Assert.False);
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
    }
}

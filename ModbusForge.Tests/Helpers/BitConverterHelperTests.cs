using System;
using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers
{
    public class BitConverterHelperTests
    {
        [Fact]
        public void ToBooleanArray_SingleByte_ExtractsBitsLSBFirst()
        {
            // Arrange
            byte[] bytes = new byte[] { 0b00000101 }; // bits 0 and 2 are true
            int count = 8;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(8, result.Length);
            Assert.True(result[0]); // bit 0
            Assert.False(result[1]); // bit 1
            Assert.True(result[2]); // bit 2
            Assert.False(result[3]); // bit 3
            Assert.False(result[4]); // bit 4
            Assert.False(result[5]); // bit 5
            Assert.False(result[6]); // bit 6
            Assert.False(result[7]); // bit 7
        }

        [Fact]
        public void ToBooleanArray_MultiByte_ExtractsBitsCorrectly()
        {
            // Arrange
            byte[] bytes = new byte[] { 0b00000001, 0b10000000 };
            int count = 16;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(16, result.Length);
            Assert.True(result[0]); // byte 0, bit 0
            for (int i = 1; i < 15; i++)
            {
                Assert.False(result[i]);
            }
            Assert.True(result[15]); // byte 1, bit 7
        }

        [Fact]
        public void ToBooleanArray_Truncation_ReturnsRequestedCount()
        {
            // Arrange
            byte[] bytes = new byte[] { 0b11111111, 0b11111111 };
            int count = 5;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(5, result.Length);
            for (int i = 0; i < 5; i++)
            {
                Assert.True(result[i]);
            }
        }

        [Fact]
        public void ToBooleanArray_Padding_FillsRemainingWithFalse()
        {
            // Arrange
            byte[] bytes = new byte[] { 0b11111111 };
            int count = 10;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(10, result.Length);
            for (int i = 0; i < 8; i++)
            {
                Assert.True(result[i]);
            }
            Assert.False(result[8]);
            Assert.False(result[9]);
        }

        [Fact]
        public void ToBooleanArray_EmptyArrayZeroCount_ReturnsEmptyArray()
        {
            // Arrange
            byte[] bytes = Array.Empty<byte>();
            int count = 0;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void ToBooleanArray_EmptyArrayPositiveCount_ReturnsFalsePaddedArray()
        {
            // Arrange
            byte[] bytes = Array.Empty<byte>();
            int count = 3;

            // Act
            bool[] result = BitConverterHelper.ToBooleanArray(bytes, count);

            // Assert
            Assert.Equal(3, result.Length);
            for (int i = 0; i < 3; i++)
            {
                Assert.False(result[i]);
            }
        }
    }
}
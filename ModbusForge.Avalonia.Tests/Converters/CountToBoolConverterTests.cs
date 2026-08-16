using System;
using System.Collections.Generic;
using ModbusForge.Avalonia.Converters;
using Xunit;

namespace ModbusForge.Avalonia.Tests.Converters
{
    public class CountToBoolConverterTests
    {
        private static readonly CountToBoolConverter Converter = CountToBoolConverter.Instance;

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(42, true)]
        public void Default_PositiveCountsOnly(int count, bool expected)
        {
            AssertExpected(Convert(count, null), expected, count, null);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(42, false)]
        public void ZeroParameter_NegatesTheDefault(int count, bool expected)
        {
            AssertExpected(Convert(count, "zero"), expected, count, "zero");
        }

        [Theory]
        [InlineData("zero", true)]
        [InlineData("ZERO", true)]
        public void ZeroParameter_IsCaseInsensitive(string parameter, bool expectedForZeroCount)
        {
            AssertExpected(Convert(0, parameter), expectedForZeroCount, 0, parameter);
        }

        [Theory]
        [InlineData("anything-else", false)]
        [InlineData("", false)]
        public void UnrecognizedParameter_BehavesLikeDefault(string parameter, bool expectedForZeroCount)
        {
            AssertExpected(Convert(0, parameter), expectedForZeroCount, 0, parameter);
        }

        [Theory]
        [InlineData("zero", false)]
        public void NullValue_IsNeverZero(string parameter, bool expected)
        {
            AssertExpected(Convert(null, parameter), expected, null, parameter);
        }

        [Fact]
        public void EnumerableValue_CountsItsElements()
        {
            var list = new List<int> { 1, 2, 3 };

            Assert.True((bool)Convert(list, null)!);
            Assert.False((bool)Convert(list, "zero")!);
            Assert.True((bool)Convert(new List<int>(), "zero")!);
        }

        [Fact]
        public void StringValue_IsNotTreatedAsEnumerableOfCharacters()
        {
            // A non-empty string must not count as "has items" for these bindings.
            Assert.False((bool)Convert("abc", null)!);
            Assert.False((bool)Convert("abc", "zero")!);
        }

        [Fact]
        public void ConvertBack_IsNotSupported()
        {
            Assert.Throws<NotSupportedException>(() =>
                Converter.ConvertBack(true, typeof(bool), null, null!));
        }

        private static bool Convert(object? value, object? parameter)
        {
            return (bool)Converter.Convert(value, typeof(bool), parameter, null!)!;
        }

        private static void AssertExpected(bool actual, bool expected, object? value, object? parameter)
        {
            var message = $"Convert({value ?? "null"}, {parameter ?? "null"}) should be {expected}, was {actual}.";
            if (expected)
            {
                Assert.True(actual, message);
            }
            else
            {
                Assert.False(actual, message);
            }
        }
    }
}

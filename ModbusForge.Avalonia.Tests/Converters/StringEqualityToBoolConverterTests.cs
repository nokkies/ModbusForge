using ModbusForge.Avalonia.Converters;
using Xunit;

namespace ModbusForge.Avalonia.Tests.Converters
{
    public class StringEqualityToBoolConverterTests
    {
        private static readonly StringEqualityToBoolConverter Converter = new();

        [Theory]
        [InlineData("SetRegister", "SetRegister", true)]
        [InlineData("setregister", "SetRegister", true)]
        [InlineData("SETREGISTER", "setregister", true)]
        [InlineData("SetCoil", "SetRegister", false)]
        [InlineData("", "SetRegister", false)]
        public void MatchesParameter_CaseInsensitively(string value, string parameter, bool expected)
        {
            Assert.Equal(expected, Converter.Convert(value, typeof(bool), parameter, null!));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(42, false)]
        public void NonStringValues_NeverMatch(object? value, bool expected)
        {
            Assert.Equal(expected, Converter.Convert(value, typeof(bool), "SetRegister", null!));
        }

        [Fact]
        public void PropertyFallback_UsedWhenParameterMissing()
        {
            var converter = new StringEqualityToBoolConverter { Expected = "LogMessage" };

            Assert.True(ConverterIsTrue(converter, "LogMessage", null));
            Assert.False(ConverterIsTrue(converter, "SetCoil", null));
        }

        [Fact]
        public void Parameter_TakesPrecedenceOverProperty()
        {
            var converter = new StringEqualityToBoolConverter { Expected = "LogMessage" };

            Assert.True(ConverterIsTrue(converter, "SetCoil", "SetCoil"));
        }

        private static bool ConverterIsTrue(StringEqualityToBoolConverter converter, string value, object? parameter)
        {
            return converter.Convert(value, typeof(bool), parameter, null!) is true;
        }
    }

    public class BoolNegateConverterTests
    {
        private static readonly BoolNegateConverter Converter = BoolNegateConverter.Instance;

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(null, true)]
        public void NegatesBoolean(object? value, bool expected)
        {
            Assert.Equal(expected, Converter.Convert(value, typeof(bool), null, null!));
        }

        [Fact]
        public void ConvertBack_IsNotSupported()
        {
            Assert.Throws<System.NotSupportedException>(() =>
                Converter.ConvertBack(true, typeof(bool), null, null!));
        }
    }
}

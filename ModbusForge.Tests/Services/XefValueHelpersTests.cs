using ModbusForge.Core.Xef;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public sealed class XefValueHelpersTests
    {
        // ---- TryParseConstant ----

        [Theory]
        [InlineData("5", false, 5.0)]
        [InlineData("-12", false, -12.0)]
        [InlineData("0", false, 0.0)]
        [InlineData("2.5", true, 2.5)]
        [InlineData("1.0E3", true, 1000.0)]
        [InlineData("1e3", true, 1000.0)]
        [InlineData("  7  ", false, 7.0)]
        public void TryParseConstant_NumericLiterals(string text, bool expectedIsReal, double expectedValue)
        {
            var parsed = XefValueHelpers.TryParseConstant(text);
            Assert.NotNull(parsed);
            Assert.Equal(expectedIsReal, parsed!.Value.IsReal);
            Assert.Equal(expectedValue, parsed.Value.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("T#5s")]
        [InlineData("Motor.Run")]
        [InlineData("1.2.3")]
        public void TryParseConstant_NonNumeric_ReturnsNull(string? text)
        {
            Assert.Null(XefValueHelpers.TryParseConstant(text));
        }

        // ---- IsTimeLiteral ----

        [Theory]
        [InlineData("T#5s", true)]
        [InlineData("t#500ms", true)]
        [InlineData("T#1h", true)]
        [InlineData("5000", false)]
        [InlineData("5", false)]
        [InlineData("TRUE", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void IsTimeLiteral_DetectsTPrefix(string? text, bool expected)
        {
            Assert.Equal(expected, XefValueHelpers.IsTimeLiteral(text));
        }

        // ---- ParseTimeToMs ----

        [Theory]
        [InlineData("T#5s", 5000)]
        [InlineData("T#500ms", 500)]
        [InlineData("T#0.5s", 500)]
        [InlineData("T#1m", 60000)]
        [InlineData("T#2m30s", 150000)]
        [InlineData("T#1h", 3600000)]
        [InlineData("T#500", 500)]
        [InlineData("5000", 5000)]
        [InlineData("t#1H", 3600000)]
        [InlineData("T#1h30m", 5400000)]
        [InlineData("T#0.25s", 250)]
        [InlineData("T#1h2m3s", 3723000)]
        public void ParseTimeToMs_SupportedForms(string text, int expectedMs)
        {
            Assert.Equal(expectedMs, XefValueHelpers.ParseTimeToMs(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("T#")]
        [InlineData("T#abc")]
        [InlineData("abc")]
        [InlineData("T#5x")]
        [InlineData("5s")]
        public void ParseTimeToMs_Garbage_ReturnsNull(string? text)
        {
            Assert.Null(XefValueHelpers.ParseTimeToMs(text));
        }

        // ---- LooksLikeEquation ----

        [Theory]
        [InlineData("2+3", true)]
        [InlineData("(1+2)*3", true)]
        [InlineData("a > 5", true)]
        [InlineData("a=b", true)]
        [InlineData("5", false)]
        [InlineData("-5", false)]
        [InlineData("2.5", false)]
        [InlineData("T#5s", false)]
        [InlineData("Motor.Run", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void LooksLikeEquation_Heuristic(string? text, bool expected)
        {
            Assert.Equal(expected, XefValueHelpers.LooksLikeEquation(text));
        }

        // ---- TryEvaluateArithmetic ----

        [Theory]
        [InlineData("2+3*4", 14.0)]
        [InlineData("(1+2)*3", 9.0)]
        [InlineData("10/4", 2.5)]
        [InlineData("-5+3", -2.0)]
        [InlineData("2*-3", -6.0)]
        [InlineData("100/10/2", 5.0)]
        [InlineData("((2+3)*4)", 20.0)]
        [InlineData("  2 + 3  ", 5.0)]
        [InlineData("1.5*2", 3.0)]
        [InlineData("2+3*4-(1+1)", 12.0)]
        [InlineData("1+((2+(3*(4))))", 15.0)]
        public void TryEvaluateArithmetic_ValidExpressions(string text, double expected)
        {
            var ok = XefValueHelpers.TryEvaluateArithmetic(text, out var result);
            Assert.True(ok);
            Assert.Equal(expected, result, 10);
        }

        [Theory]
        [InlineData("a+b")]
        [InlineData("a>5")]
        [InlineData("10/0")]
        [InlineData("5 x 2")]
        [InlineData("sin(30)")]
        [InlineData("1==2")]
        [InlineData("(1+2")]
        [InlineData("1+2)")]
        [InlineData("+")]
        [InlineData("*")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("1 2")]
        public void TryEvaluateArithmetic_Unsupported_ReturnsFalse(string? text)
        {
            var ok = XefValueHelpers.TryEvaluateArithmetic(text, out var result);
            Assert.False(ok);
            Assert.Equal(0.0, result);
        }

        // ---- MapDataType ----

        [Theory]
        [InlineData("BOOL", PlcArea.Coil, false, true)]
        [InlineData("bool", PlcArea.Coil, false, true)]
        [InlineData("BYTE", PlcArea.HoldingRegister, false, false)]
        [InlineData("WORD", PlcArea.HoldingRegister, false, false)]
        [InlineData("DINT", PlcArea.HoldingRegister, false, false)]
        [InlineData("UDINT", PlcArea.HoldingRegister, false, false)]
        [InlineData("INT", PlcArea.HoldingRegister, false, false)]
        [InlineData("UINT", PlcArea.HoldingRegister, false, false)]
        [InlineData("REAL", PlcArea.HoldingRegister, true, false)]
        [InlineData("LREAL", PlcArea.HoldingRegister, true, false)]
        [InlineData("TIME", PlcArea.HoldingRegister, false, false)]
        [InlineData("DATE", PlcArea.HoldingRegister, false, false)]
        [InlineData("TIME_OF_DAY", PlcArea.HoldingRegister, false, false)]
        [InlineData("SomeUnknownType", PlcArea.HoldingRegister, false, false)]
        [InlineData(null, PlcArea.HoldingRegister, false, false)]
        [InlineData("", PlcArea.HoldingRegister, false, false)]
        public void MapDataType_KnownTypes(
            string? typeName, PlcArea expectedArea, bool expectedIsReal, bool expectedIsBool)
        {
            var (area, isReal, isBool) = XefTypeMapper.MapDataType(typeName);
            Assert.Equal(expectedArea, area);
            Assert.Equal(expectedIsReal, isReal);
            Assert.Equal(expectedIsBool, isBool);
        }

        // ---- Predicate helpers ----

        [Theory]
        [InlineData("BOOL", true, false, false)]
        [InlineData("bool", true, false, false)]
        [InlineData("REAL", false, true, false)]
        [InlineData("LREAL", false, true, false)]
        [InlineData("INT", false, false, true)]
        [InlineData("WORD", false, false, true)]
        [InlineData("DINT", false, false, true)]
        [InlineData("UDINT", false, false, true)]
        [InlineData("BYTE", false, false, true)]
        [InlineData("UINT", false, false, true)]
        [InlineData("TIME", false, false, false)]
        [InlineData("DATE", false, false, false)]
        [InlineData("Nothing", false, false, false)]
        [InlineData(null, false, false, false)]
        [InlineData("", false, false, false)]
        public void TypePredicates_MatchSemantics(
            string? typeName, bool expectedBool, bool expectedReal, bool expectedInt)
        {
            Assert.Equal(expectedBool, XefTypeMapper.IsBoolType(typeName));
            Assert.Equal(expectedReal, XefTypeMapper.IsRealType(typeName));
            Assert.Equal(expectedInt, XefTypeMapper.IsIntegerType(typeName));
        }
    }
}

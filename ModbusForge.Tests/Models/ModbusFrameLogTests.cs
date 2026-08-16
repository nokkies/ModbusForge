using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Models
{
    public class ModbusFrameLogTests
    {
        [Theory]
        [InlineData(true, true, false, false)]
        [InlineData(false, false, true, false)]
        [InlineData(null, false, false, true)]
        public void CrcStates_AreExactlyOneOfThree(bool? isValidCrc, bool expectValid, bool expectInvalid, bool expectNotApplicable)
        {
            var frame = new ModbusFrameLog { IsValidCrc = isValidCrc };

            Assert.Equal(expectValid, frame.IsCrcValid);
            Assert.Equal(expectInvalid, frame.IsCrcInvalid);
            Assert.Equal(expectNotApplicable, frame.IsCrcNotApplicable);

            var set = new[] { frame.IsCrcValid, frame.IsCrcInvalid, frame.IsCrcNotApplicable };
            Assert.Equal(1, set.Count(b => b));
        }

        [Fact]
        public void HexString_FormatsBytesAsSpaceDelimitedUpperHex()
        {
            var frame = new ModbusFrameLog { RawBytes = new byte[] { 0x01, 0x03, 0xFF, 0x00 } };

            Assert.Equal("01 03 FF 00", frame.HexString);
        }

        [Fact]
        public void HexString_IsEmptyForNoBytes()
        {
            var frame = new ModbusFrameLog();

            Assert.Equal(string.Empty, frame.HexString);
        }
    }
}

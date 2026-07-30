using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public sealed class ModbusAddressValidatorTests
    {
        private readonly IModbusAddressValidator _validator = new ModbusAddressValidator();

        [Theory]
        [InlineData(0, 1, true)]
        [InlineData(1, 1, true)]
        [InlineData(65535, 1, true)]
        [InlineData(65530, 6, true)]
        [InlineData(0, 125, true)]
        [InlineData(0, 126, false)]
        [InlineData(65535, 2, false)]
        [InlineData(0, 0, false)]
        [InlineData(0, -1, false)]
        [InlineData(-1, 1, false)]
        [InlineData(65536, 1, false)]
        [InlineData(0, 65536, false)]
        public void IsValidRange_Returns_Expected(int startAddress, int count, bool expected)
        {
            Assert.Equal(expected, _validator.IsValidRange(startAddress, count));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(247, true)]
        [InlineData(0, false)]
        [InlineData(248, false)]
        [InlineData(255, false)]
        public void IsValidUnitId_Returns_Expected(byte unitId, bool expected)
        {
            Assert.Equal(expected, _validator.IsValidUnitId(unitId));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(65535, true)]
        [InlineData(-1, false)]
        [InlineData(65536, false)]
        public void IsValidStartAddress_Returns_Expected(int startAddress, bool expected)
        {
            Assert.Equal(expected, _validator.IsValidStartAddress(startAddress));
        }

        [Fact]
        public void ValidateOrThrow_Does_Not_Throw_For_Valid_Input()
        {
            var exception = Record.Exception(() => ModbusAddressValidator.ValidateOrThrow(1, 0, 5));
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(0, 0, 1)]
        [InlineData(248, 0, 1)]
        [InlineData(1, -1, 1)]
        [InlineData(1, 65535, 2)]
        [InlineData(1, 0, 126)]
        public void ValidateOrThrow_Throws_For_Invalid_Input(byte unitId, int startAddress, int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ModbusAddressValidator.ValidateOrThrow(unitId, startAddress, count));
        }
    }
}

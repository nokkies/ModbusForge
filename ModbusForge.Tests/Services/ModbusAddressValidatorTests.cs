using ModbusForge.Models;
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

        [Theory]
        [InlineData(PlcArea.HoldingRegister, false, 125, true)]
        [InlineData(PlcArea.HoldingRegister, false, 126, false)]
        [InlineData(PlcArea.HoldingRegister, true, 123, true)]
        [InlineData(PlcArea.HoldingRegister, true, 124, false)]
        [InlineData(PlcArea.Coil, false, 2000, true)]
        [InlineData(PlcArea.Coil, false, 2001, false)]
        [InlineData(PlcArea.Coil, true, 1968, true)]
        [InlineData(PlcArea.Coil, true, 1969, false)]
        [InlineData(PlcArea.DiscreteInput, false, 2000, true)]
        [InlineData(PlcArea.DiscreteInput, false, 2001, false)]
        public void IsValidCount_AreaAware_Returns_Expected(PlcArea area, bool isWrite, int count, bool expected)
        {
            Assert.Equal(expected, _validator.IsValidCount(count, area, isWrite));
        }
    }
}

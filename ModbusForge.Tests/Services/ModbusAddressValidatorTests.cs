using System.Linq;
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
        [InlineData(PlcArea.InputRegister, false, 125, true)]
        [InlineData(PlcArea.InputRegister, false, 126, false)]
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

        [Theory]
        [InlineData(0, 1, true)]
        [InlineData(0, 125, true)]
        [InlineData(0, 126, true)]
        [InlineData(0, 65536, true)]
        [InlineData(65535, 1, true)]
        [InlineData(65535, 2, false)]
        [InlineData(65530, 6, true)]
        [InlineData(65530, 7, false)]
        [InlineData(0, 65537, false)]
        [InlineData(0, 0, false)]
        [InlineData(-1, 1, false)]
        public void IsValidAddressRange_Returns_Expected(int startAddress, int count, bool expected)
        {
            Assert.Equal(expected, _validator.IsValidAddressRange(startAddress, count));
        }

        [Theory]
        [InlineData(PlcArea.HoldingRegister, false, 125)]
        [InlineData(PlcArea.HoldingRegister, true, 123)]
        [InlineData(PlcArea.InputRegister, false, 125)]
        [InlineData(PlcArea.Coil, false, 2000)]
        [InlineData(PlcArea.Coil, true, 1968)]
        [InlineData(PlcArea.DiscreteInput, false, 2000)]
        public void GetMaxCountPerRequest_Returns_Expected(PlcArea area, bool isWrite, int expected)
        {
            Assert.Equal(expected, _validator.GetMaxCountPerRequest(area, isWrite));
        }

        [Theory]
        [InlineData(0, 300, 3, 125, 125, 50)]
        [InlineData(0, 125, 1, 125, 0, 0)]
        [InlineData(100, 250, 2, 125, 125, 25)]
        [InlineData(0, 100, 1, 100, 0, 0)]
        public void GetReadRanges_Splits_Into_Expected_Chunks(
            int start, int count, int expectedChunks, int firstChunk, int secondChunk, int thirdChunk)
        {
            var ranges = _validator.GetReadRanges(start, count, PlcArea.HoldingRegister).ToList();
            Assert.Equal(expectedChunks, ranges.Count);
            if (ranges.Count > 0) Assert.Equal(firstChunk, ranges[0].Count);
            if (ranges.Count > 1) Assert.Equal(secondChunk, ranges[1].Count);
            if (ranges.Count > 2) Assert.Equal(thirdChunk, ranges[2].Count);

            // total must match requested count
            Assert.Equal(count, ranges.Sum(r => r.Count));
            Assert.Equal(start, ranges.First().StartAddress);
            Assert.Equal(start + count, ranges.Last().StartAddress + ranges.Last().Count);
        }

        [Fact]
        public void GetReadRanges_For_Large_HoldingRegister_Request_Produces_Multiple_125_Chunks()
        {
            const int start = 1;
            const int count = 600;
            var ranges = _validator.GetReadRanges(start, count, PlcArea.HoldingRegister).ToList();

            Assert.Equal(5, ranges.Count);
            Assert.All(ranges.Take(4), r => Assert.Equal(125, r.Count));
            Assert.Equal(501, ranges.Last().StartAddress);
            Assert.Equal(100, ranges.Last().Count);
            Assert.Equal(start + count, ranges.Last().StartAddress + ranges.Last().Count);
        }

        [Fact]
        public void GetReadRanges_For_Coil_Request_Uses_2000_Max()
        {
            const int count = 4000;
            var ranges = _validator.GetReadRanges(0, count, PlcArea.Coil).ToList();

            Assert.Equal(2, ranges.Count);
            Assert.Equal(2000, ranges[0].Count);
            Assert.Equal(2000, ranges[1].Count);
        }

        [Theory]
        [InlineData(0, 65537)]
        [InlineData(-1, 1)]
        [InlineData(0, 0)]
        public void GetReadRanges_Throws_For_Invalid_Range(int start, int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _validator.GetReadRanges(start, count, PlcArea.HoldingRegister).ToList());
        }
    }
}

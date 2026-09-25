using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Located variables live in the Modbus tables the way a Modicon Quantum maps its
    /// state RAM: %M = coil, %I = discrete input, %IW = input register, %MW = holding
    /// register, all at the same 1-based number; 32-bit values keep the low word first.
    /// </summary>
    public class PlcMemoryMappingTests
    {
        [Fact]
        public void IntOnMw_IsTheHoldingRegisterWithTheSameNumber()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Speed", "INT", "%MW100")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=-2|OUT=Speed"))
                .Project()).Scan();

            Assert.Equal(0xFFFE, plc.Store.HoldingRegisters[100]);
            Assert.Equal(-2, plc.Int("Speed"));
        }

        [Fact]
        public void BoolOnMwBit_IsThatBitOfTheHoldingRegister()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Flag3", "BOOL", "%MW50.3")
                .Variable("Flag15", "BOOL", "%MW50.15")
                .Project());
            plc.Store.HoldingRegisters[50] = 0x0101;

            plc.Runtime.Write("Flag3", PlcOps.True);
            plc.Runtime.Write("Flag15", PlcOps.True);

            Assert.Equal(0x8109, plc.Store.HoldingRegisters[50]);
            plc.Runtime.Write("Flag3", PlcOps.False);
            Assert.Equal(0x8101, plc.Store.HoldingRegisters[50]);
        }

        [Fact]
        public void EboolOnM_IsTheCoil_AndOnI_IsTheDiscreteInput()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("StartPb", "EBOOL", "%I7")
                .Variable("MotorRun", "EBOOL", "%M12")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=StartPb|OUT=MotorRun"))
                .Project());

            plc.Store.InputDiscretes[7] = true;
            plc.Scan();

            Assert.True(plc.Store.CoilDiscretes[12]);
            Assert.False(plc.Store.CoilDiscretes[7]);
        }

        [Fact]
        public void IntOnIw_IsTheInputRegister()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Level", "INT", "%IW3")
                .Variable("LevelCopy", "INT", "%MW3")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=Level|OUT=LevelCopy"))
                .Project());

            plc.Store.InputRegisters[3] = 1234;
            plc.Scan();

            Assert.Equal(1234, plc.Store.HoldingRegisters[3]);
        }

        [Fact]
        public void RealOnMw_StoresTheLowWordFirst()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Flow", "REAL", "%MW100")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=1.0|OUT=Flow"))
                .Project()).Scan();

            // 1.0f is 16#3F80_0000.
            Assert.Equal(0x0000, plc.Store.HoldingRegisters[100]);
            Assert.Equal(0x3F80, plc.Store.HoldingRegisters[101]);
            Assert.Equal(1.0, plc.Real("Flow"));
        }

        [Fact]
        public void DintOnMw_StoresTheLowWordFirst()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Count", "DINT", "%MW200")
                .Project());

            plc.Runtime.Write("Count", PlcValue.FromInteger(PlcType.Dint, 0x12345678));

            Assert.Equal(0x5678, plc.Store.HoldingRegisters[200]);
            Assert.Equal(0x1234, plc.Store.HoldingRegisters[201]);
        }

        [Fact]
        public void StructureOnMw_FollowsTheQuantumMappingRules()
        {
            // INT on an even byte; BOOLs on consecutive bytes; REAL back on an even byte.
            var plc = new PlcHarness(new XefBuilder()
                .Raw(@"<DDTSource DDTName=""tPump""><structure>
                    <variables name=""Speed"" typeName=""INT""/>
                    <variables name=""Run"" typeName=""BOOL""/>
                    <variables name=""Fault"" typeName=""BOOL""/>
                    <variables name=""Flow"" typeName=""REAL""/>
                  </structure></DDTSource>")
                .Variable("Pump1", "tPump", "%MW300")
                .Project());

            plc.Runtime.Write("Pump1.Speed", PlcValue.FromInteger(PlcType.Int, 7));
            plc.Runtime.Write("Pump1.Fault", PlcOps.True);
            plc.Runtime.Write("Pump1.Flow", PlcValue.FromReal(1.0));

            Assert.Equal(7, plc.Store.HoldingRegisters[300]);
            Assert.Equal(0x0100, plc.Store.HoldingRegisters[301]);
            Assert.Equal(0x0000, plc.Store.HoldingRegisters[302]);
            Assert.Equal(0x3F80, plc.Store.HoldingRegisters[303]);
            Assert.False(plc.Bool("Pump1.Run"));
        }

        [Fact]
        public void ArrayOnMw_IndexesConsecutiveRegisters()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Table", "ARRAY[1..4] OF INT", "%MW400")
                .Variable("i", "INT", init: "3")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=99|OUT=Table[i]"))
                .Project()).Scan();

            Assert.Equal(99, plc.Store.HoldingRegisters[402]);
        }

        [Fact]
        public void BitExtractionOnAWord_ReadsAndWritesThatBit()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("Status", "WORD", "%MW20")
                .Variable("Bit4", "BOOL", "%M1")
                .Section("S", Block(".1", "MOVE", 2, 2, "IN=TRUE|OUT=Status.2") + Block(".2", "MOVE", 2, 12, "IN=Status.4|OUT=Bit4"))
                .Project());

            plc.Store.HoldingRegisters[20] = 0x0010;
            plc.Scan();

            Assert.Equal(0x0014, plc.Store.HoldingRegisters[20]);
            Assert.True(plc.Store.CoilDiscretes[1]);
        }

        [Fact]
        public void TopologicalIo_StaysOutOfTheModbusTables()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Variable("FieldInput", "EBOOL", @"%I\2.2\1.7.1")
                .Project());

            plc.Runtime.Write("FieldInput", PlcOps.True);

            Assert.True(plc.Bool("FieldInput"));
            Assert.False(plc.Store.InputDiscretes[1]);
            Assert.False(plc.Store.InputDiscretes[2]);
        }

        [Theory]
        [InlineData("%MW100", PlcMemoryArea.HoldingRegister, 100, null)]
        [InlineData("%MW63310.15", PlcMemoryArea.HoldingRegister, 63310, 15)]
        [InlineData("%M137", PlcMemoryArea.Coil, 137, null)]
        [InlineData("%I225", PlcMemoryArea.DiscreteInput, 225, null)]
        [InlineData("%IW80", PlcMemoryArea.InputRegister, 80, null)]
        [InlineData("%IW200.15", PlcMemoryArea.InputRegister, 200, 15)]
        [InlineData("%S21", PlcMemoryArea.SystemBit, 21, null)]
        [InlineData("%SW160.6", PlcMemoryArea.SystemWord, 160, 6)]
        [InlineData(@"%I\2.2\1.7.1", PlcMemoryArea.Private, 0, null)]
        [InlineData("%I1.16.4", PlcMemoryArea.Private, 0, null)]
        [InlineData("%QW1.14.2", PlcMemoryArea.Private, 0, null)]
        public void DirectAddresses_ParseToTheirArea(string text, PlcMemoryArea area, int index, int? bit)
        {
            var address = PlcAddress.TryParse(text);

            Assert.NotNull(address);
            Assert.Equal(area, address!.Area);
            Assert.Equal(index, address.Index);
            Assert.Equal(bit, address.Bit);
        }
    }
}

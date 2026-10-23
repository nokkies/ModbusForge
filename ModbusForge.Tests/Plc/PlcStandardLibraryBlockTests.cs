using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// Blocks of the Control Expert Standard library, as the Standard Block Library
    /// manual describes them (Schneider Electric 33002519.20, 09/2020).
    /// </summary>
    public class PlcStandardLibraryBlockTests
    {
        private static PlcHarness Fbd(string network, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            return new PlcHarness(xef.Section("S", network).Project());
        }

        private static PlcHarness St(string code, params (string Name, string Type)[] variables)
        {
            var xef = new XefBuilder();
            foreach (var (name, type) in variables) xef.Variable(name, type);
            return new PlcHarness(xef.StSection("Code", code).Project());
        }

        private static void SetInts(PlcHarness plc, string array, int lower, params int[] values)
        {
            for (var i = 0; i < values.Length; i++)
                plc.Runtime.Write($"{array}[{lower + i}]", PlcValue.FromInteger(PlcType.AnyInteger, values[i]));
        }

        private static long[] Ints(PlcHarness plc, string array, int lower, int count)
            => Enumerable.Range(lower, count).Select(i => plc.Int($"{array}[{i}]")).ToArray();

        private static bool Overflow(PlcHarness plc) => plc.Bool("%S18");

        // ---- FIND_*_AR* (pp. 63-78): rank counted from 0 at the start of the array ----

        [Fact]
        public void FindEq_GivesTheRankFromTheStartOfTheArray_OrMinusOne()
        {
            var plc = Fbd(Block(".1", "FIND_EQ_ARINT", 2, 2, "IN1=T,IN2=V|OUT=R"),
                ("T", "ARRAY[1..5] OF INT"), ("V", "INT"), ("R", "INT"));
            SetInts(plc, "T", 1, 4, 7, 9, 7, 2);

            plc.Runtime.Write("V", PlcValue.FromInteger(PlcType.Int, 7));
            plc.Scan();
            Assert.Equal(1, plc.Int("R")); // T[2] is the second element: rank 1

            plc.Runtime.Write("V", PlcValue.FromInteger(PlcType.Int, 5));
            plc.Scan();
            Assert.Equal(-1, plc.Int("R"));
        }

        [Fact]
        public void FindGt_FindsTheFirstGreaterElement()
        {
            var plc = Fbd(Block(".1", "FIND_GT_ARREAL", 2, 2, "IN1=T,IN2=2.5|OUT=R"),
                ("T", "ARRAY[0..3] OF REAL"), ("R", "INT"));
            for (var i = 0; i < 4; i++) plc.Runtime.Write($"T[{i}]", PlcValue.FromReal(new[] { 1.0, 2.5, 3.0, 9.0 }[i]));

            plc.Scan();

            Assert.Equal(2, plc.Int("R"));
        }

        [Fact]
        public void FindLt_FindsTheFirstSmallerElement()
        {
            var plc = Fbd(Block(".1", "FIND_LT_ARDINT", 2, 2, "IN1=T,IN2=0|OUT=R"),
                ("T", "ARRAY[0..2] OF DINT"), ("R", "INT"));
            SetInts(plc, "T", 0, 5, -3, -9);

            plc.Scan();

            Assert.Equal(1, plc.Int("R"));
        }

        [Fact]
        public void Find_OnAnInvalidReal_GivesMinusTwo_AndSetsS18()
        {
            var plc = Fbd(Block(".1", "FIND_GT_ARREAL", 2, 2, "IN1=T,IN2=1.0|OUT=R"),
                ("T", "ARRAY[0..1] OF REAL"), ("R", "INT"));
            plc.Runtime.Write("T[0]", PlcValue.FromReal(double.NaN));

            plc.Scan();

            Assert.Equal(-2, plc.Int("R"));
            Assert.True(Overflow(plc));
        }

        // ---- ROL_AR* / ROR_AR* (pp. 107-110) -----------------------------------------

        [Fact]
        public void RolAr_ShiftsTowardsHigherIndexes_Rotating()
        {
            // "With a shift register of 2, the element previously situated in first position
            // goes to third ... the second last goes to first position and the last to second."
            var plc = Fbd(Block(".1", "ROL_ARINT", 2, 2, "N=2,INOUT=T|INOUT=T"), ("T", "ARRAY[1..5] OF INT"));
            SetInts(plc, "T", 1, 1, 2, 3, 4, 5);

            plc.Scan();

            Assert.Equal(new long[] { 4, 5, 1, 2, 3 }, Ints(plc, "T", 1, 5));
        }

        [Fact]
        public void RorAr_ShiftsTowardsLowerIndexes_Rotating()
        {
            var plc = Fbd(Block(".1", "ROR_ARDINT", 2, 2, "N=1,INOUT=T|INOUT=T"), ("T", "ARRAY[0..4] OF DINT"));
            SetInts(plc, "T", 0, 1, 2, 3, 4, 5);

            plc.Scan();

            Assert.Equal(new long[] { 2, 3, 4, 5, 1 }, Ints(plc, "T", 0, 5));
        }

        [Fact]
        public void RolAr_WithZeroOrNegativePositions_LeavesTheArray()
        {
            var plc = Fbd(Block(".1", "ROL_ARINT", 2, 2, "N=-1,INOUT=T|INOUT=T"), ("T", "ARRAY[0..2] OF INT"));
            SetInts(plc, "T", 0, 1, 2, 3);

            plc.Scan();

            Assert.Equal(new long[] { 1, 2, 3 }, Ints(plc, "T", 0, 3));
        }

        [Fact]
        public void RolAr_CalledFromSt_TakesPositionsThenTheArray()
        {
            var plc = St("ROL_ARINT(1, T);", ("T", "ARRAY[0..2] OF INT"));
            SetInts(plc, "T", 0, 1, 2, 3);

            plc.Scan();

            Assert.Equal(new long[] { 3, 1, 2 }, Ints(plc, "T", 0, 3));
        }

        // ---- INC / DEC (pp. 247, 261) -----------------------------------------------

        [Fact]
        public void Inc_AddsOne_AndOnOverrunWrapsAndSetsS18()
        {
            var plc = Fbd(Block(".1", "INC", 2, 2, "INOUT=V|INOUT=V"), ("V", "INT"));
            plc.Runtime.Write("V", PlcValue.FromInteger(PlcType.Int, 41));
            plc.Scan();
            Assert.Equal(42, plc.Int("V"));
            Assert.False(Overflow(plc));

            plc.Runtime.Write("V", PlcValue.FromInteger(PlcType.Int, short.MaxValue));
            plc.Scan();
            Assert.Equal(short.MinValue, plc.Int("V"));
            Assert.True(Overflow(plc));
        }

        [Fact]
        public void Dec_OnAnUnsignedZero_WrapsToTheMaximum()
        {
            var plc = Fbd(Block(".1", "DEC", 2, 2, "INOUT=V|INOUT=V"), ("V", "UINT"));

            plc.Scan();

            Assert.Equal(ushort.MaxValue, plc.Int("V"));
            Assert.True(Overflow(plc));
        }

        [Fact]
        public void Inc_CalledFromSt_UpdatesItsVariable()
        {
            var plc = St("INC(V); INC(INOUT := V);", ("V", "DINT"));

            plc.Scan();

            Assert.Equal(2, plc.Int("V"));
        }

        // ---- AVE (pp. 297-299) -----------------------------------------------------

        [Fact]
        public void Ave_IsTheWeightedMeanOfItsPairs()
        {
            // (K1, X1) = (1, 10), (K2, X2) = (3, 20): (10 + 60) / 4.
            var plc = Fbd(Block(".1", "AVE_REAL", 2, 2, "K_X1=1.0,K_X2=10.0,K_X3=3.0,K_X4=20.0|Y=Y"), ("Y", "REAL")).Scan();

            Assert.Equal(17.5, plc.Real("Y"), 3);
        }

        [Fact]
        public void Ave_OnIntegers_DropsTheFraction()
        {
            var plc = Fbd(Block(".1", "AVE", 2, 2, "K_X1=A,K_X2=B,K_X3=C,K_X4=D|Y=Y"),
                ("A", "INT"), ("B", "INT"), ("C", "INT"), ("D", "INT"), ("Y", "INT"));
            foreach (var (name, value) in new[] { ("A", 1), ("B", 10), ("C", 3), ("D", 20) })
                plc.Runtime.Write(name, PlcValue.FromInteger(PlcType.Int, value));

            plc.Scan();

            Assert.Equal(17, plc.Int("Y"));
        }

        [Fact]
        public void Ave_WithAZeroWeightSum_SetsS18()
        {
            var plc = Fbd(Block(".1", "AVE_REAL", 2, 2, "K_X1=0.0,K_X2=10.0|Y=Y"), ("Y", "REAL")).Scan();

            Assert.True(Overflow(plc));
        }

        [Fact]
        public void Ave_WithAnOddNumberOfInputs_SetsEnoToZero()
        {
            var plc = Fbd(Block(".1", "AVE_REAL", 2, 2, "K_X1=1.0,K_X2=10.0,K_X3=3.0|Y=Y"), ("Y", "REAL")).Scan();

            Assert.False(plc.BlockNamed(".1").LastEno);
        }

        // ---- EXTRACT (pp. 187-190) ---------------------------------------------------

        private static PlcHarness Extract(string parameters)
        {
            var plc = Fbd(Block(".1", "EXTRACT", 2, 2, parameters + "|DST=D,ERR=E"),
                ("S", "ARRAY[0..9] OF WORD"), ("D", "ARRAY[0..4] OF WORD"), ("E", "INT"));
            SetInts(plc, "S", 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
            SetInts(plc, "D", 0, 100, 101, 102, 103, 104);
            return plc;
        }

        [Fact]
        public void Extract_CopiesPartsIntoTheDestination_LeavingTheRest()
        {
            var plc = Extract("SRC=S,SRC_NELEM=10,SRC_POS=2,DST_POS=1,CPY_NELEM=3").Scan();

            Assert.Equal(new long[] { 100, 2, 3, 4, 104 }, Ints(plc, "D", 0, 5));
            Assert.Equal(0, plc.Int("E"));
        }

        [Fact]
        public void Extract_PartsSizeFollowsSrcNelem()
        {
            // 20 bytes in 5 parts: each part is 4 bytes, two WORDs.
            var plc = Extract("SRC=S,SRC_NELEM=5,SRC_POS=1,DST_POS=0,CPY_NELEM=2").Scan();

            Assert.Equal(new long[] { 2, 3, 4, 5, 104 }, Ints(plc, "D", 0, 5));
        }

        [Theory]
        [InlineData("SRC_NELEM=0,SRC_POS=0,DST_POS=0,CPY_NELEM=1", -1)]
        [InlineData("SRC_NELEM=3,SRC_POS=0,DST_POS=0,CPY_NELEM=1", -2)]
        [InlineData("SRC_NELEM=10,SRC_POS=10,DST_POS=0,CPY_NELEM=1", -3)]
        [InlineData("SRC_NELEM=10,SRC_POS=0,DST_POS=5,CPY_NELEM=1", -4)]
        public void Extract_WithInvalidParameters_CopiesNothing(string parameters, int error)
        {
            var plc = Extract("SRC=S," + parameters).Scan();

            Assert.Equal(error, plc.Int("E"));
            Assert.Equal(new long[] { 100, 101, 102, 103, 104 }, Ints(plc, "D", 0, 5));
        }

        [Fact]
        public void Extract_ShortensACopyThatDoesNotFit()
        {
            var plc = Extract("SRC=S,SRC_NELEM=10,SRC_POS=0,DST_POS=3,CPY_NELEM=5").Scan();

            Assert.Equal(new long[] { 100, 101, 102, 0, 1 }, Ints(plc, "D", 0, 5));
            Assert.Equal(2, plc.Int("E"));
        }

        [Fact]
        public void Extract_WithNothingToCopy_ReportsIt()
        {
            var plc = Extract("SRC=S,SRC_NELEM=10,SRC_POS=0,DST_POS=0,CPY_NELEM=0").Scan();

            Assert.Equal(1, plc.Int("E"));
        }
    }
}

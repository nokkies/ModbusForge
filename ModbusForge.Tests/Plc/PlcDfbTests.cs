using ModbusForge.Core.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Plc
{
    /// <summary>
    /// User function blocks (DFBs) whose code the XEF contains run it per instance,
    /// whether the code is FBD or ST and whether the instance is called from FBD or ST.
    /// </summary>
    public class PlcDfbTests
    {
        // A DFB with FBD code: Q := A AND B.
        internal const string AndDfb = @"<FBSource nameOfFBType=""MyAnd"">
  <inputParameters><variables name=""A"" typeName=""BOOL""/><variables name=""B"" typeName=""BOOL""/></inputParameters>
  <outputParameters><variables name=""Q"" typeName=""BOOL""/></outputParameters>
  <FBProgram name=""Body""><FBDSource><networkFBD>
    <FFBBlock instanceName="".1"" typeName=""AND"" enEnO=""false"" width=""7"" height=""6""><objPosition posX=""2"" posY=""2""/>
      <descriptionFFB execAfter="""">
        <inputVariable invertedPin=""false"" formalParameter=""IN1"" effectiveParameter=""A""/>
        <inputVariable invertedPin=""false"" formalParameter=""IN2"" effectiveParameter=""B""/>
        <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Q""/>
      </descriptionFFB></FFBBlock>
  </networkFBD></FBDSource></FBProgram>
</FBSource>";

        // A DFB with ST code and a nested TON: Count counts Pulse edges, Late is Pulse held 300 ms.
        internal const string CounterDfb = @"<EFBSource nameOfEFBType=""TON""><ExternalToolsOnly>
  <inputParameters><variables name=""IN"" typeName=""BOOL""/><variables name=""PT"" typeName=""TIME""/></inputParameters>
  <outputParameters><variables name=""Q"" typeName=""BOOL""/><variables name=""ET"" typeName=""TIME""/></outputParameters>
</ExternalToolsOnly></EFBSource>
<FBSource nameOfFBType=""Counter"">
  <inputParameters><variables name=""Pulse"" typeName=""EBOOL""/></inputParameters>
  <outputParameters><variables name=""Count"" typeName=""INT""/><variables name=""Late"" typeName=""BOOL""/></outputParameters>
  <privateLocalVariables>
    <variables name=""Step"" typeName=""INT""><variableInit value=""1""/></variables>
    <variables name=""Hold"" typeName=""TON""/>
  </privateLocalVariables>
  <FBProgram name=""Body""><STSource>IF RE(Pulse) THEN Count := Count + Step; END_IF;
Hold(IN := Pulse, PT := t#300ms);
Late := Hold.Q;</STSource></FBProgram>
</FBSource>";

        // A DFB with an in/out parameter: every call adds 1 to the caller's variable.
        private const string BumpDfb = @"<FBSource nameOfFBType=""Bump"">
  <inOutParameters><variables name=""Val"" typeName=""INT""/></inOutParameters>
  <FBProgram name=""Body""><STSource>Val := Val + 1;</STSource></FBProgram>
</FBSource>";

        // A DFB whose code is encrypted in the XEF: its interface is exported, its code is not.
        internal const string SecretDfb = @"<FBSource nameOfFBType=""Secret""><crypted>00</crypted><ExternalToolsOnly>
                    <outputParameters><variables name=""Q"" typeName=""BOOL""/></outputParameters></ExternalToolsOnly></FBSource>";

        [Fact]
        public void FbdDfb_CalledFromFbd_RunsItsOwnCode()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(AndDfb)
                .Variable("Inst", "MyAnd").Variable("X", "BOOL").Variable("Y", "BOOL").Variable("Z", "BOOL", "%M9")
                .Section("S", Block("Inst", "MyAnd", 2, 2, "A=X,B=Y|Q=Z"))
                .Project());
            Assert.True(plc.BlockNamed("Inst").IsSimulated);

            plc.Runtime.Write("X", PlcOps.True);
            plc.Scan();
            Assert.False(plc.Store.CoilDiscretes[9]);

            plc.Runtime.Write("Y", PlcOps.True);
            plc.Scan();
            Assert.True(plc.Store.CoilDiscretes[9]);
        }

        [Fact]
        public void StDfb_WithANestedTimer_KeepsStatePerInstance()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(CounterDfb)
                .Variable("C1", "Counter").Variable("C2", "Counter").Variable("P1", "BOOL").Variable("P2", "BOOL")
                .Variable("N1", "INT").Variable("N2", "INT").Variable("L1", "BOOL")
                .Section("S",
                    Block("C1", "Counter", 2, 2, "Pulse=P1|Count=N1,Late=L1") +
                    Block("C2", "Counter", 2, 12, "Pulse=P2|Count=N2,Late"))
                .Project());

            plc.Runtime.Write("P1", PlcOps.True);
            plc.Scan(2);
            Assert.Equal(1, plc.Int("N1"));
            Assert.Equal(0, plc.Int("N2"));
            Assert.False(plc.Bool("L1"));

            plc.Scan(2);
            Assert.True(plc.Bool("L1"));

            plc.Runtime.Write("P1", PlcOps.False);
            plc.Scan();
            plc.Runtime.Write("P1", PlcOps.True);
            plc.Scan();
            Assert.Equal(2, plc.Int("N1"));
        }

        [Fact]
        public void DfbDefaults_ApplyToEveryInstance()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(CounterDfb).Variable("C1", "Counter").Variable("C2", "Counter").Project());

            Assert.Equal(1, plc.Int("C1.Step"));
            Assert.Equal(1, plc.Int("C2.Step"));
        }

        [Fact]
        public void Dfb_CalledFromSt_RunsItsCode()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(AndDfb)
                .Variable("Inst", "MyAnd").Variable("Z", "BOOL")
                .StSection("Code", "Inst(A := TRUE, B := TRUE); Z := Inst.Q;")
                .Project()).Scan();

            Assert.True(plc.Bool("Z"));
        }

        [Fact]
        public void InOutParameters_AreCopiedBackToTheCallersVariable()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(BumpDfb)
                .Variable("B1", "Bump").Variable("V", "INT", "%MW7")
                .StSection("Code", "B1(Val := V);")
                .Project());

            plc.Scan(3);

            Assert.Equal(3, plc.Store.HoldingRegisters[7]);
        }

        [Fact]
        public void OneInstanceCalledTwiceFromSt_WorksOnEachCallersInOutVariable()
        {
            // IEC 61131-3 passes VAR_IN_OUT by reference on every call, so each call
            // bumps the variable that call passes.
            var plc = new PlcHarness(new XefBuilder().Raw(BumpDfb)
                .Variable("B1", "Bump").Variable("V", "INT").Variable("W", "INT")
                .StSection("Code", "B1(Val := V); B1(Val := W);")
                .Project());

            plc.Scan(2);

            Assert.Equal(2, plc.Int("V"));
            Assert.Equal(2, plc.Int("W"));
        }

        [Fact]
        public void OneInstanceInTwoFbdBlocks_WorksOnEachBlocksInOutVariable()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(BumpDfb)
                .Variable("B1", "Bump").Variable("V", "INT").Variable("W", "INT", "%MW20")
                .Section("S",
                    Block("B1", "Bump", 2, 2, "Val=V|Val=V") +
                    Block("B1", "Bump", 2, 12, "Val=W|Val=W"))
                .Project());

            plc.Scan(2);

            Assert.Equal(2, plc.Int("V"));
            Assert.Equal(2, plc.Store.HoldingRegisters[20]);
        }

        [Fact]
        public void InOutWithAnotherVariableOnItsOutput_CopiesTheValueThere()
        {
            // 35006144 (FBD, VAR_IN_OUT variable): with different variables on the in/out
            // input and output, the input variable's value is copied to the output one.
            var plc = new PlcHarness(new XefBuilder().Raw(BumpDfb)
                .Variable("B1", "Bump").Variable("V", "INT").Variable("W", "INT")
                .Section("S", Block("B1", "Bump", 2, 2, "Val=V|Val=W"))
                .Project());

            plc.Scan(2);

            Assert.Equal(2, plc.Int("V"));
            Assert.Equal(2, plc.Int("W"));
        }

        [Fact]
        public void InOutBoundToAnArrayElement_FollowsTheIndexOnEachCall()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(BumpDfb)
                .Variable("B1", "Bump").Variable("I", "INT")
                .Variable("Tab", "ARRAY[1..3] OF INT")
                .StSection("Code", "FOR I := 1 TO 3 DO B1(Val := Tab[I]); END_FOR;")
                .Project());

            plc.Scan(2);

            Assert.Equal(2, plc.Int("Tab[1]"));
            Assert.Equal(2, plc.Int("Tab[2]"));
            Assert.Equal(2, plc.Int("Tab[3]"));
        }

        [Fact]
        public void EncryptedDfb_StaysInert()
        {
            var plc = new PlcHarness(new XefBuilder()
                .Raw(SecretDfb)
                .Variable("S1", "Secret").Variable("Out", "BOOL")
                .Section("S", Block("S1", "Secret", 2, 2, "|Q=Out"))
                .Project()).Scan();

            Assert.False(plc.BlockNamed("S1").IsSimulated);
            Assert.False(plc.Bool("Out"));
        }

        [Fact]
        public void EncryptedDfb_IsAProtectedShell()
        {
            var plc = new PlcHarness(new XefBuilder().Raw(SecretDfb).Raw(AndDfb)
                .Variable("S1", "Secret").Variable("A1", "MyAnd").Variable("Out", "BOOL").Variable("Q", "BOOL")
                .Section("S", Block("S1", "Secret", 2, 2, "|Q=Out") + Block("A1", "MyAnd", 20, 2, "A=TRUE,B=TRUE|Q=Q"))
                .Project());

            Assert.True(plc.BlockNamed("S1").IsProtectedDfb);
            Assert.True(plc.BlockNamed("A1").IsSimulated);
            Assert.False(plc.BlockNamed("A1").IsProtectedDfb);
            Assert.Equal(new[] { "Secret" }, plc.Runtime.Project.Types!.ProtectedDfbTypes);
        }

        [Fact]
        public void ProtectedDfbTypes_AreListedInXefOrder()
        {
            var alpha = SecretDfb.Replace(@"nameOfFBType=""Secret""", @"nameOfFBType=""Alpha""");
            var project = new XefBuilder().Raw(alpha).Raw(AndDfb).Raw(SecretDfb)
                .Variable("A1", "Alpha").Variable("Out", "BOOL")
                .Section("S", Block("A1", "Alpha", 2, 2, "|Q=Out"))
                .Project();

            Assert.Equal(new[] { "Alpha", "Secret" }, project.Types!.ProtectedDfbTypes);
        }

        [Fact]
        public void LdOnlyDfb_IsListedButNotComputed()
        {
            // The runtime runs FBD and ST sections only, so a DFB whose only code is LD computes nothing.
            var project = new XefBuilder()
                .Raw(@"<FBSource nameOfFBType=""Ladder""><FBProgram name=""Rungs""><LDSource/></FBProgram></FBSource>")
                .Variable("L1", "Ladder")
                .Section("S", Block("L1", "Ladder", 2, 2, ""))
                .Project();

            var block = Assert.Single(project.Blocks);
            Assert.False(block.IsSimulated);
            Assert.False(block.IsProtectedDfb);
            Assert.Equal(new PlcBlockCounts(1, 0, 0, 1), PlcBlockCounts.Of(project.Blocks));
        }

        [Fact]
        public void BlockCounts_TellEmptyProtectedDfbsFromUnsupportedTypes()
        {
            var project = new XefBuilder().Raw(SecretDfb).Raw(AndDfb)
                .Variable("S1", "Secret").Variable("A1", "MyAnd").Variable("Out", "BOOL").Variable("Q", "BOOL")
                .Section("S", Block("S1", "Secret", 2, 2, "|Q=Out") + Block("A1", "MyAnd", 20, 2, "A=TRUE,B=TRUE|Q=Q")
                    + Block(".3", "NO_SUCH_BLOCK", 40, 2, "IN=Q|OUT=Out"))
                .Project();

            var counts = PlcBlockCounts.Of(project.Blocks);

            Assert.Equal(new PlcBlockCounts(3, 1, 1, 1), counts);
            Assert.Equal("1 of 3 blocks computed, 1 empty (protected DFB), 1 not simulated (unsupported type)", counts.Describe());
        }

        [Theory]
        [InlineData(2, 2, 0, 0, "2 of 2 blocks computed")]
        [InlineData(2, 1, 1, 0, "1 of 2 blocks computed, 1 empty (protected DFB)")]
        [InlineData(2, 1, 0, 1, "1 of 2 blocks computed, 1 not simulated (unsupported type)")]
        public void BlockCounts_LeaveOutTheClausesWithNoBlocks(int total, int computed, int emptyProtectedDfb, int unsupported, string expected)
        {
            Assert.Equal(expected, new PlcBlockCounts(total, computed, emptyProtectedDfb, unsupported).Describe());
        }
    }
}

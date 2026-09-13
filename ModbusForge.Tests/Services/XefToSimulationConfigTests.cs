using System.Xml.Linq;
using ModbusForge.Core.Xef;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Tests for <see cref="XefToSimulationConfig.Translate"/>. The fixtures hand-build
    /// <see cref="XefProgram"/>/<see cref="XefProject"/> shapes that mirror the confirmed XEF
    /// grammar (see <see cref="XefParserTests.FbdXml"/>), plus one XML-driven case parsed through
    /// <see cref="XefParser.Parse"/> to exercise the real parser-to-translator path.
    /// </summary>
    public sealed class XefToSimulationConfigTests
    {
        private const string LogicXml = @"
<RSLogixContent>
  <contentHeader name=""Logic""/>
  <dataBlock>
    <variables name=""Motor1"" typeName=""BOOL"" topologicalAddress=""10""/>
    <variables name=""Level"" typeName=""REAL"" topologicalAddress=""40""/>
    <variables name=""Motor1Run"" typeName=""BOOL"" topologicalAddress=""20"">
      <variableInit value=""false""/>
    </variables>
  </dataBlock>
  <program>
    <identProgram name=""Main""/>
    <FBDSource>
      <networkFBD>
        <FFBBlock instanceName=""TON1"" typeName=""TON"" enEnO=""true"">
          <objPosition posX=""100"" posY=""120""/>
          <inputVariable formalParameter=""IN"" effectiveParameter=""Motor1""/>
          <inputVariable formalParameter=""PT"" effectiveParameter=""T#5s""/>
          <outputVariable formalParameter=""Q"" effectiveParameter=""Motor1Run""/>
        </FFBBlock>
        <FFBBlock instanceName=""AND1"" typeName=""AND"" enEnO=""false"">
          <objPosition posX=""400"" posY=""120""/>
          <inputVariable formalParameter=""IN1""/>
          <inputVariable formalParameter=""IN2"" effectiveParameter=""Level""/>
          <outputVariable formalParameter=""Q""/>
        </FFBBlock>
        <FFBBlock instanceName=""OR1"" typeName=""OR"" enEnO=""false"">
          <objPosition posX=""400"" posY=""300""/>
          <inputVariable formalParameter=""IN1""/>
          <inputVariable formalParameter=""IN2""/>
          <outputVariable formalParameter=""Q""/>
        </FFBBlock>
        <FFBBlock instanceName=""NOT1"" typeName=""NOT"" enEnO=""false"">
          <objPosition posX=""400"" posY=""480""/>
          <inputVariable formalParameter=""IN""/>
          <outputVariable formalParameter=""Q""/>
        </FFBBlock>
        <linkFB>
          <linkSource parentObjectName=""TON1"" pinName=""Q""/>
          <linkDestination parentObjectName=""AND1"" pinName=""IN1""/>
        </linkFB>
      </networkFBD>
    </FBDSource>
  </program>
</RSLogixContent>";

        // ---- (a) logic block type mapping ----

        [Fact]
        public void Translate_LogicBlocks_MapsToRightElementTypes()
        {
            var (project, program) = ParseAndFind(LogicXml, "Main");

            var result = XefToSimulationConfig.Translate(program, project);

            Assert.False(result.IsStProgram);
            var and = result.Config.Nodes.First(n => n.Id == "AND1");
            var or = result.Config.Nodes.First(n => n.Id == "OR1");
            var not = result.Config.Nodes.First(n => n.Id == "NOT1");

            Assert.Equal(PlcElementType.AND, and.ElementType);
            Assert.Equal(PlcElementType.OR, or.ElementType);
            Assert.Equal(PlcElementType.NOT, not.ElementType);

            // All three are mapped (not unsupported).
            Assert.Empty(result.UnsupportedBlocks);
            Assert.Equal(4, result.MappedBlocks.Count);
        }

        // ---- (b) TON1.Q -> AND1.IN1 link ----

        [Fact]
        public void Translate_TonToAndLink_ProducesOutputToInput1Connection()
        {
            var (project, program) = ParseAndFind(LogicXml, "Main");

            var result = XefToSimulationConfig.Translate(program, project);

            // The TON1.Q -> AND1.IN1 link must be present (there are also helper-node wires,
            // so locate it explicitly rather than assuming it is the only connection).
            var conn = result.Config.Connections
                .First(c => c.SourceNodeId == "TON1" && c.TargetNodeId == "AND1");
            Assert.Equal("Output", conn.SourceConnector);
            Assert.Equal("Input1", conn.TargetConnector);
        }

        // ---- (c) a pin bound to a tag with a topologicalAddress ----

        [Fact]
        public void Translate_TagBoundPin_SetsPlcAddressReferenceOnRightPort()
        {
            var (project, program) = ParseAndFind(LogicXml, "Main");

            var result = XefToSimulationConfig.Translate(program, project);

            // TON1's PT = "T#5s" is a time constant, not an address -> TimerPresetMs.
            var ton = result.Config.Nodes.First(n => n.Id == "TON1");
            Assert.Equal(5000, ton.TimerPresetMs);

            // TON1.IN = "Motor1" (BOOL, topologicalAddress 10). The TON block has no address
            // bindings, so the tag becomes a helper InputBool node bound to coil 10, wired to the TON.
            var motorHelper = result.Config.Nodes.First(n => n.Name == "Motor1" && n.Id != "TON1");
            Assert.Equal(PlcElementType.InputBool, motorHelper.ElementType);
            Assert.Equal(PlcArea.Coil, motorHelper.Input1Address!.Area);
            Assert.Equal(10, motorHelper.Input1Address.Address);

            // AND1.IN2 = "Level" (REAL, topologicalAddress 40). The AND block has no address
            // bindings, so the tag becomes a helper InputInt node bound to holding register 40.
            var levelHelper = result.Config.Nodes.First(n => n.Name == "Level");
            Assert.Equal(PlcElementType.InputInt, levelHelper.ElementType);
            Assert.Equal(PlcArea.HoldingRegister, levelHelper.Input1Address!.Area);
            Assert.Equal(40, levelHelper.Input1Address.Address);

            // The helpers must be wired into their host blocks (the level helper feeds the AND's
            // second input port, the Motor1 helper feeds the TON's input port).
            Assert.Contains(result.Config.Connections,
                c => c.SourceNodeId == motorHelper.Id && c.TargetNodeId == "TON1");
            var levelWire = Assert.Single(result.Config.Connections,
                c => c.SourceNodeId == levelHelper.Id && c.TargetNodeId == "AND1");
            Assert.Equal("Input2", levelWire.TargetConnector);
        }

        // ---- (c-extra) a tag bound to the output pin ----

        [Fact]
        public void Translate_TagBoundOutputPin_SetsOutputAddress()
        {
            var (project, program) = ParseAndFind(LogicXml, "Main");

            var result = XefToSimulationConfig.Translate(program, project);

            // TON1.Q = "Motor1Run" (BOOL, topologicalAddress 20). The TON block has no output
            // address binding, so the tag becomes a helper OutputBool node bound to coil 20,
            // wired from the TON's output.
            var runOut = result.Config.Nodes.First(n => n.Name == "Motor1Run");
            Assert.Equal(PlcElementType.OutputBool, runOut.ElementType);
            Assert.Equal(PlcArea.Coil, runOut.OutputAddress!.Area);
            Assert.Equal(20, runOut.OutputAddress.Address);
            Assert.Contains(result.Config.Connections,
                c => c.SourceNodeId == "TON1" && c.TargetNodeId == runOut.Id);
        }

        // ---- (d) unsupported FB type ----

        [Fact]
        public void Translate_UnsupportedBlockType_LandsInUnsupportedWithMarkerAndNote()
        {
            const string xml = @"
<RSLogixContent>
  <contentHeader name=""X""/>
  <dataBlock>
    <variables name=""A"" typeName=""BOOL"" topologicalAddress=""5""/>
  </dataBlock>
  <program>
    <identProgram name=""P""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""MYFB"" typeName=""MYFB"" enEnO=""false"">
        <objPosition posX=""0"" posY=""0""/>
        <inputVariable formalParameter=""IN"" effectiveParameter=""A""/>
        <outputVariable formalParameter=""Q""/>
      </FFBBlock>
    </networkFBD></FBDSource>
  </program>
</RSLogixContent>";

            var project = XefParser.Parse(XElement.Parse(xml));
            var program = project.FindProgram("P")!;
            var block = program.FindBlock("MYFB")!;

            var result = XefToSimulationConfig.Translate(program, project);

            // The block is still emitted as a node with the Unsupported marker...
            var node = Assert.Single(result.Config.Nodes);
            Assert.Equal("MYFB", node.Id);
            Assert.Equal(PlcElementType.Unsupported, node.ElementType);

            // ...and is reported as unsupported with a note.
            Assert.Same(block, Assert.Single(result.UnsupportedBlocks));
            Assert.Empty(result.MappedBlocks);
            Assert.Contains(result.Notes, n => n.Contains("MYFB") && n.Contains("no simulation equivalent"));
        }

        // ---- ST program: zero nodes, flag set, no crash ----

        [Fact]
        public void Translate_StProgram_ReturnsZeroNodesWithFlag()
        {
            const string xml = @"
<Root>
  <contentHeader name=""STPLC""/>
  <program>
    <identProgram name=""Calc""/>
    <STSource>
      IF Start THEN
        Run := TRUE;
      END_IF;
    </STSource>
  </program>
</Root>";

            var project = XefParser.Parse(XElement.Parse(xml));
            var program = project.FindProgram("Calc")!;

            var result = XefToSimulationConfig.Translate(program, project);

            Assert.True(result.IsStProgram);
            Assert.Empty(result.Config.Nodes);
            Assert.Empty(result.MappedBlocks);
            Assert.Empty(result.UnsupportedBlocks);
        }

        // ---- real vs int comparator variant ----

        [Fact]
        public void Translate_RealComparator_UsesRealElementType()
        {
            const string xml = @"
<Root>
  <contentHeader name=""C""/>
  <dataBlock>
    <variables name=""Lvl"" typeName=""REAL"" topologicalAddress=""30""/>
  </dataBlock>
  <program>
    <identProgram name=""P""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""EQ1"" typeName=""EQ"" enEnO=""false"">
        <objPosition posX=""0"" posY=""0""/>
        <inputVariable formalParameter=""IN1"" effectiveParameter=""Lvl""/>
        <inputVariable formalParameter=""IN2"" effectiveParameter=""1.5""/>
        <outputVariable formalParameter=""Q""/>
      </FFBBlock>
      <FFBBlock instanceName=""EQ2"" typeName=""EQ_REAL"" enEnO=""false"">
        <objPosition posX=""0"" posY=""200""/>
        <inputVariable formalParameter=""IN1""/>
        <inputVariable formalParameter=""IN2"" effectiveParameter=""2.5""/>
        <outputVariable formalParameter=""Q""/>
      </FFBBlock>
    </networkFBD></FBDSource>
  </program>
</Root>";

            var project = XefParser.Parse(XElement.Parse(xml));
            var program = project.FindProgram("P")!;

            var result = XefToSimulationConfig.Translate(program, project);

            var eq1 = result.Config.Nodes.First(n => n.Id == "EQ1");
            var eq2 = result.Config.Nodes.First(n => n.Id == "EQ2");

            // "EQ" (no real indicator) -> int comparator; "EQ_REAL" -> real comparator.
            Assert.Equal(PlcElementType.COMPARE_EQ, eq1.ElementType);
            Assert.Equal(PlcElementType.COMPARE_EQ_REAL, eq2.ElementType);
        }

        // ---- canvas sizing + defaults ----

        [Fact]
        public void Translate_SetsDefaultsAndSizesCanvas()
        {
            var (project, program) = ParseAndFind(LogicXml, "Main");

            var result = XefToSimulationConfig.Translate(program, project);
            var config = result.Config;

            Assert.Equal(100, config.ScanIntervalMs);
            Assert.True(config.ShowLiveValues);

            // AND1 is at PosX=400 -> X=200, Width=240 -> right edge 440 -> canvas >= 480.
            Assert.True(config.CanvasWidth >= 440 + 40, $"CanvasWidth {config.CanvasWidth} too small");
            Assert.True(config.CanvasHeight >= 440 + 40, $"CanvasHeight {config.CanvasHeight} too small");
        }

        // ---- helpers ----

        private static (XefProject, XefProgram) ParseAndFind(string xml, string programName)
        {
            var project = XefParser.Parse(XElement.Parse(xml));
            var program = project.FindProgram(programName)
                ?? throw new InvalidOperationException($"Program '{programName}' not parsed from fixture.");
            return (project, program);
        }
    }
}

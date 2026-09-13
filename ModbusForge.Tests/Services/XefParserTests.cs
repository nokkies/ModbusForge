using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Core.Xef;
using ModbusForge.Data;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public sealed class XefParserTests
    {
        // A realistic XEF fragment covering the confirmed grammar: contentHeader, sectionDesc,
        // dataBlock/variables (with topologicalAddress), an FBSource type signature, a DDTSource
        // user data type, and a program with FBD blocks (FFBBlock) + links (linkFB).
        private const string FbdXml = @"
<RSLogixContent>
  <contentHeader name=""HGPLC001""/>
  <sectionDesc name=""Main"" FMName=""Main"" FMId=""1"" FMOrder=""1"" SectionOrder=""1""/>
  <dataBlock>
    <variables name=""Motor1"" typeName=""BOOL"" topologicalAddress=""10""/>
    <variables name=""Level"" typeName=""REAL"" topologicalAddress=""40""/>
    <variables name=""Motor1Run"" typeName=""BOOL"" topologicalAddress=""20"">
      <variableInit value=""false""/>
      <comment>Motor run command</comment>
    </variables>
  </dataBlock>
  <FBSource nameOfFBType=""TON"">
    <inputParameters>
      <variables name=""IN"" typeName=""BOOL""/>
      <variables name=""PT"" typeName=""TIME""/>
    </inputParameters>
    <outputParameters>
      <variables name=""Q"" typeName=""BOOL""/>
    </outputParameters>
  </FBSource>
  <DDTSource DDTName=""MotorType"">
    <structure>
      <variables name=""Run"" typeName=""BOOL""/>
      <variables name=""Speed"" typeName=""REAL""/>
    </structure>
  </DDTSource>
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
        <linkFB>
          <linkSource parentObjectName=""TON1"" pinName=""Q""/>
          <linkDestination parentObjectName=""AND1"" pinName=""IN1""/>
        </linkFB>
        <textBox width=""200"" height=""40""><objPosition posX=""100"" posY=""10""/><text>Startup logic</text></textBox>
      </networkFBD>
    </FBDSource>
  </program>
</RSLogixContent>";

        [Fact]
        public void Parse_FbdProject_ExtractsNamesTasksAndVariables()
        {
            var project = XefParser.Parse(XElement.Parse(FbdXml));

            Assert.Equal("HGPLC001", project.Name);
            Assert.Single(project.Tasks);
            Assert.Equal("Main", project.Tasks[0].Name);
            Assert.Equal(3, project.Variables.Count);

            var motor = project.FindVariable("Motor1");
            Assert.NotNull(motor);
            Assert.Equal("BOOL", motor!.DataType);
            Assert.Equal("10", motor.Address);
            // The area is derived from the data type: a BOOL topological address is a Coil.
            Assert.Equal((ModbusForge.Models.PlcArea.Coil, 10), motor.TryParseAddress());

            var level = project.FindVariable("Level");
            Assert.NotNull(level);
            Assert.Equal("REAL", level!.DataType);
            // A REAL topological address is a Holding Register.
            Assert.Equal((ModbusForge.Models.PlcArea.HoldingRegister, 40), level.TryParseAddress());

            var run = project.FindVariable("Motor1Run");
            Assert.Equal("false", run!.Value);
            Assert.Equal("Motor run command", run.Comment);
        }

        [Fact]
        public void Parse_FbdProject_ParsesBlockTypeAndDatatype()
        {
            var project = XefParser.Parse(XElement.Parse(FbdXml));

            var ton = project.BlockTypes.FirstOrDefault(b => b.Name == "TON");
            Assert.NotNull(ton);
            Assert.Contains(ton!.Inputs, p => p.Name == "IN");
            Assert.Contains(ton.Inputs, p => p.Name == "PT");
            Assert.Contains(ton.Outputs, p => p.Name == "Q");

            var dt = project.DataTypes.FirstOrDefault(d => d.Name == "MotorType");
            Assert.NotNull(dt);
            Assert.Equal(2, dt!.Members.Count);
        }

        [Fact]
        public void Parse_FbdProject_ParsesProgramBlocksPinsAndLinks()
        {
            var project = XefParser.Parse(XElement.Parse(FbdXml));

            var program = project.FindProgram("Main");
            Assert.NotNull(program);
            Assert.Equal("FBD", program!.Language);
            Assert.Equal(2, program.Blocks.Count);
            Assert.Single(program.Links);
            Assert.Single(program.Comments);
            Assert.Equal("Startup logic", program.Comments[0].Text);

            var ton1 = program.FindBlock("TON1");
            Assert.NotNull(ton1);
            Assert.Equal("TON", ton1!.TypeName);
            Assert.Equal(100, ton1.PosX);
            Assert.Equal(120, ton1.PosY);

            // Pin effective parameters.
            var inPin = ton1.FindInput("IN");
            Assert.Equal("Motor1", inPin!.EffectiveParameter);
            var ptPin = ton1.FindInput("PT");
            Assert.Equal("T#5s", ptPin!.EffectiveParameter);
            var qPin = ton1.FindOutput("Q");
            Assert.Equal("Motor1Run", qPin!.EffectiveParameter);

            // The link connects TON1.Q -> AND1.IN1.
            var link = program.Links[0];
            Assert.Equal("TON1", link.SourceBlock);
            Assert.Equal("Q", link.SourcePin);
            Assert.Equal("AND1", link.DestBlock);
            Assert.Equal("IN1", link.DestPin);
        }

        [Fact]
        public void Parse_UnknownBlockType_RaisesWarning()
        {
            // A block whose typeName is not in the FB/EF source list.
            const string xml = @"
<Root>
  <contentHeader name=""X""/>
  <FBSource nameOfFBType=""AND""/>
  <program>
    <identProgram name=""P""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""MYSTERY"" typeName=""MY_MYSTERY_FB"" enEnO=""false"">
        <objPosition posX=""0"" posY=""0""/>
      </FFBBlock>
    </networkFBD></FBDSource>
  </program>
</Root>";

            var project = XefParser.Parse(XElement.Parse(xml));
            Assert.Contains(project.Warnings, w => w.Contains("MY_MYSTERY_FB"));
        }

        [Fact]
        public void Parse_StProgram_DetectsStSource()
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

            var program = project.FindProgram("Calc");
            Assert.Equal("ST", program!.Language);
            Assert.True(project.HasStPrograms);
            Assert.Contains("Run := TRUE", program.StSource);
        }

        [Fact]
        public void ParseFile_ZipWrappedXef_IsUnzippedAndParsed()
        {
            // Build a ZIP whose member is the FBD XML named "proj.xef" (the .ZEF layout).
            var path = Path.Combine(Path.GetTempPath(), $"xef_{Guid.NewGuid():N}.zef");
            try
            {
                using (var fs = File.Create(path))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = zip.CreateEntry("proj.xef");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(FbdXml);
                }

                var project = XefParser.ParseFile(path);
                Assert.Equal("HGPLC001", project.Name);
                Assert.Equal(3, project.Variables.Count);
                Assert.Equal(path, project.SourcePath);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ParseFile_MissingFile_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => XefParser.ParseFile("C:\\does\\not\\exist.xef"));
        }

        [Fact]
        public void ParseFile_NotXml_ThrowsInvalidData()
        {
            var path = Path.Combine(Path.GetTempPath(), $"xef_{Guid.NewGuid():N}.xef");
            try
            {
                File.WriteAllText(path, "This is not XML");
                Assert.Throws<InvalidDataException>(() => XefParser.ParseFile(path));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void Parse_RealXefFile_ParsesSuccessfully()
        {
            // A snapshot of a real XEF project is parsed, and the presence of the expected
            // content header and at least one FBD program is verified.
            var path = "C:/Users/rvn/source/repos/UnityParseEngine/resources/XML/LCPLC001.XEF";
            if (!File.Exists(path))
            {
                return;
            }

            var project = XefParser.ParseFile(path);
            Assert.Equal("LCPLC001", project.Name);
            Assert.True(project.Tasks.Count > 0);
            Assert.True(project.Variables.Count > 0);
            Assert.True(project.BlockTypes.Count > 0);

            // The project must contain at least one FBD program.
            Assert.Contains(project.Programs, p => p.Language == "FBD");
        }

        [Fact]
        public void Translate_RealXefFile_ProducesNodesWithoutThrowing()
        {
            var path = "C:/Users/rvn/source/repos/UnityParseEngine/resources/XML/LCPLC001.XEF";
            if (!File.Exists(path))
            {
                return;
            }

            var project = XefParser.ParseFile(path);
            Assert.Equal("LCPLC001", project.Name);

            // Select an FBD program (i.e., one whose body is not ST) present in the project.
            var program = project.Programs.
                First(p => p.Language == "FBD" && p.Blocks.Count > 0);

            // The key check: the translator must not throw, and must produce at least one
            // visual node when applied to a real, non-trivial project.
            var graph = XefToSimulationConfig.Translate(program, project);
            Assert.NotNull(graph);
            Assert.True(graph.Config.Nodes.Count > 0);
        }

        [Fact]
        public void ParseSampleLadder_ReadsRungsAndElements()
        {
            var path = "C:/Users/rvn/source/repos/ModbusForge/testdata/SampleLadder.XEF";
            Assert.True(File.Exists(path), $"Sample ladder XEF missing at {path}");

            var project = XefParser.ParseFile(path);
            Assert.True(project.HasLadderPrograms, "Sample should contain ladder programs");

            var motor = project.Programs.First(p => p.Name == "MotorControl");
            Assert.Equal("LD", motor.Language);
            Assert.True(motor.IsLadder);
            Assert.Equal(5, motor.Rungs.Count);

            // Network 1: NO start, NC stop, coil.
            var net1 = motor.Rungs[0];
            Assert.Equal(3, net1.Elements.Count);
            Assert.Equal(LadderElementKind.Contact, net1.Elements[0].Kind);
            Assert.Equal("Motor.Start", net1.Elements[0].Tag);
            Assert.Equal(LadderElementKind.ContactNegated, net1.Elements[1].Kind);
            Assert.Equal("Motor.Stop", net1.Elements[1].Tag);
            Assert.Equal(LadderElementKind.Coil, net1.Elements[2].Kind);
            Assert.Equal("Motor.Run", net1.Elements[2].Tag);

            // Network 2: contact + inline TON block + coil.
            var net2 = motor.Rungs[1];
            var block = net2.Elements.Single(e => e.IsBlock);
            Assert.Equal("TON", block.BlockType);
            Assert.Equal("tmrRun", block.Tag);

            // Network 3: rising-edge contact + set coil; a parallel branch with NC + reset.
            var net3 = motor.Rungs[2];
            Assert.Equal(LadderElementKind.ContactRise, net3.Elements[0].Kind);
            Assert.Equal(LadderElementKind.CoilSet, net3.Elements[1].Kind);
            Assert.Equal(LadderElementKind.CoilReset, net3.Elements[3].Kind);
            Assert.Equal(2, net3.Elements.Single(e => e.Kind == LadderElementKind.CoilReset).Row);
        }
    }
}

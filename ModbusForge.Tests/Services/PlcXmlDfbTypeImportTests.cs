using System;
using System.Collections.ObjectModel;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Tests.Plc;
using static ModbusForge.Tests.Plc.XefBuilder;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// The user function block (DFB) types of an XEF: their interface, their code sections
    /// (FBD canvases, ST and IL text, other languages listed only), and that they are listed
    /// but never run.
    /// </summary>
    public class PlcXmlDfbTypeImportTests
    {
        // A DFB with no code and one input: not encrypted, and no FBProgram.
        private const string EmptyDfb = @"<FBSource nameOfFBType=""Blank"">
  <inputParameters><variables name=""Go"" typeName=""BOOL""/></inputParameters>
</FBSource>";

        // A DFB with an LD section, which the importer lists without a canvas.
        private const string LadderDfb = @"<FBSource nameOfFBType=""Rungs"">
  <FBProgram name=""Body""><LDSource></LDSource></FBProgram>
</FBSource>";

        // Code under a protected type: no corpus file carries any, so this FBD and ST pair (Q := A AND B) is a defensive case.
        private const string LockedFbdProgram = @"<FBProgram name=""Body""><FBDSource><networkFBD>
  <FFBBlock instanceName="".1"" typeName=""AND"" enEnO=""false"" width=""7"" height=""6""><objPosition posX=""2"" posY=""2""/>
    <descriptionFFB execAfter="""">
      <inputVariable invertedPin=""false"" formalParameter=""IN1"" effectiveParameter=""A""/>
      <inputVariable invertedPin=""false"" formalParameter=""IN2"" effectiveParameter=""B""/>
      <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Q""/>
    </descriptionFFB></FFBBlock>
</networkFBD></FBDSource></FBProgram>";

        private const string LockedStProgram = @"<FBProgram name=""Body""><STSource>Q := A AND B;</STSource></FBProgram>";

        [Fact]
        public void ExportedDfbTypes_AreListedWithTheirInterface()
        {
            var result = Import(new XefBuilder().Raw(PlcDfbTests.AndDfb).Raw(PlcDfbTests.CounterDfb));

            Assert.Equal(new[] { "MyAnd", "Counter" }, result.DfbTypes.Select(t => t.Name));

            var myAnd = result.DfbTypes[0];
            Assert.Equal(new[] { "A", "B" }, myAnd.Inputs.Select(v => v.Name));
            Assert.Equal(new[] { "Q" }, myAnd.Outputs.Select(v => v.Name));
            Assert.Empty(myAnd.InOuts);
            Assert.False(myAnd.IsProtected);

            var counter = result.DfbTypes[1];
            Assert.Equal(new[] { "Step", "Hold" }, counter.PrivateVariables.Select(v => v.Name));
            Assert.Equal("1", Assert.Single(counter.PrivateVariables, v => v.Name == "Step").InitialValue);
            Assert.Equal("TON", Assert.Single(counter.PrivateVariables, v => v.Name == "Hold").TypeName);
        }

        [Fact]
        public void AnFbdSection_GetsACanvasKeyedByTypeAndSection()
        {
            var section = Assert.Single(Import(new XefBuilder().Raw(PlcDfbTests.AndDfb)).DfbTypes.Single().Sections);

            Assert.Equal("Body", section.Name);
            Assert.Equal("FBD", section.Language);
            Assert.Equal("MyAnd/Body", section.EditorKey);
            Assert.Single(section.Canvas!.Nodes, n => n.Plc?.TypeName == "AND");
            Assert.Null(section.SourceText);
        }

        [Fact]
        public void AnStSection_KeepsItsText()
        {
            var section = Assert.Single(Import(new XefBuilder().Raw(PlcDfbTests.CounterDfb))
                .DfbTypes.Single(t => t.Name == "Counter").Sections);

            Assert.Equal("ST", section.Language);
            Assert.Null(section.Canvas);
            Assert.StartsWith("IF RE(Pulse) THEN", section.SourceText!);
        }

        [Fact]
        public void AProtectedType_IsAShellWithItsInterface()
        {
            var secret = Import(new XefBuilder().Raw(PlcDfbTests.SecretDfb)).DfbTypes.Single();

            Assert.True(secret.IsProtected);
            Assert.Equal(new[] { "Q" }, secret.Outputs.Select(v => v.Name));
            Assert.Empty(secret.Sections);
            Assert.Empty(secret.PrivateVariables);
        }

        [Fact]
        public void AProtectedTypeWithFbdCodeInItsXml_KeepsNoSections()
            => AssertProtectedKeepsNoSections(LockedFbdProgram);

        [Fact]
        public void AProtectedTypeWithStCodeInItsXml_KeepsNoSections()
            => AssertProtectedKeepsNoSections(LockedStProgram);

        [Fact]
        public void ATypeWithoutCode_IsNotProtected()
        {
            var blank = Import(new XefBuilder().Raw(EmptyDfb)).DfbTypes.Single();

            Assert.False(blank.IsProtected);
            Assert.Empty(blank.Sections);
            Assert.Single(blank.Inputs);
        }

        [Fact]
        public void AnotherLanguage_IsListedWithoutACanvas()
        {
            var section = Assert.Single(Import(new XefBuilder().Raw(LadderDfb)).DfbTypes.Single().Sections);

            Assert.Equal("LD", section.Language);
            Assert.Equal("Rungs/Body", section.EditorKey);
            Assert.Null(section.Canvas);
            Assert.Null(section.SourceText);
        }

        [Fact]
        public void DfbCanvases_NeverShareNodeIdsWithPrograms_AndAreNotRun()
        {
            var result = Import(new XefBuilder().Raw(PlcDfbTests.AndDfb)
                .Variable("X", "BOOL").Variable("Y", "BOOL").Variable("Z", "BOOL")
                .Section("S", Block(".1", "AND", 2, 2, "IN1=X,IN2=Y|OUT=Z")));

            var canvasIds = result.DfbTypes.SelectMany(t => t.Sections).Select(s => s.Canvas!)
                .SelectMany(c => c.Nodes).Select(n => n.Id).ToList();
            var programIds = result.Sections.SelectMany(s => s.Nodes).Select(n => n.Id).ToList();

            Assert.NotEmpty(canvasIds);
            Assert.Empty(canvasIds.Intersect(programIds));
            Assert.All(canvasIds, id => Assert.StartsWith("D", id));
            Assert.Equal(new[] { "S" }, result.Project!.Sections.Select(s => s.Name));
        }

        [Fact]
        public void ADfbCanvasWithRepeatedInstanceNames_GetsDistinctIds()
        {
            // Each network names its blocks ".1" and ".2", as Control Expert does.
            var canvas = Import(new XefBuilder().Raw(FbdType("Twin",
                    Block(".1", "AND", 2, 2, "IN1=A,IN2=B|OUT") + Block(".2", "NOT", 20, 2, "IN|OUT=Q") + Link(".1", "OUT", ".2", "IN"),
                    Block(".1", "OR", 2, 2, "IN1=A,IN2=B|OUT") + Block(".2", "AND", 20, 2, "IN1|OUT=Q") + Link(".1", "OUT", ".2", "IN1"))))
                .DfbTypes.Single().Sections.Single().Canvas!;

            var ids = canvas.Nodes.Select(n => n.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());

            // Each wire still joins the blocks of its own network: AND to NOT, then OR to AND.
            var byId = canvas.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            string TypeOf(string id) => byId[id].Plc?.TypeName ?? "";
            var wires = canvas.Connections.Select(c => (TypeOf(c.SourceNodeId), TypeOf(c.TargetNodeId))).ToList();
            Assert.Equal(new[] { ("AND", "NOT"), ("OR", "AND") }, wires);
        }

        [Fact]
        public void DfbCanvasNodes_GetNoLiveValues_WhileRunning()
        {
            var result = Import(new XefBuilder().Raw(PlcDfbTests.AndDfb)
                .Variable("X", "BOOL").Variable("Y", "BOOL").Variable("Z", "BOOL")
                .Section("S", Block(".1", "AND", 2, 2, "IN1=X,IN2=Y|OUT=Z")));
            var canvasNodes = result.DfbTypes.Single().Sections.Single().Canvas!.Nodes;
            var programNodes = result.Sections.Single().Nodes;

            using var service = new PlcRuntimeService();
            service.Load(result.Project);
            service.SetScanIntervalMs(10_000);
            service.Start(new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>(canvasNodes.Concat(programNodes)),
                ScanIntervalMs = 10_000
            });
            service.Tick(TimeSpan.FromMilliseconds(100));

            // The program's block gets live values, which shows the check can see them.
            Assert.All(canvasNodes, n => Assert.Null(n.PlcLive));
            Assert.All(programNodes, n => Assert.NotNull(n.PlcLive));
        }

        [Fact]
        public void DfbWarnings_NameTheirType()
        {
            var result = Import(new XefBuilder().Raw(FbdType("Broken",
                Block(".1", "AND", 2, 2, "IN1=A,IN2=B|OUT") + Link(".1", "OUT", ".9", "IN"))));

            Assert.Contains("DFB Broken: Link references unknown instance: .1 -> .9", result.Warnings);
        }

        [Fact]
        public void AnEmptyDfbCanvas_WarnsWithoutParenthesesForItsTask()
        {
            var result = Import(new XefBuilder().Raw(FbdType("Blank", "")));

            Assert.Contains("DFB Blank: Section 'Body' produced no nodes.", result.Warnings);
        }

        [Fact]
        public void ARepeatedInterfaceName_KeepsTheFirstDeclaration()
        {
            var type = Import(new XefBuilder().Raw(@"<FBSource nameOfFBType=""Twice"">
  <inputParameters><variables name=""A"" typeName=""BOOL""/></inputParameters>
  <ExternalToolsOnly><inputParameters><variables name=""A"" typeName=""INT""/></inputParameters></ExternalToolsOnly>
</FBSource>")).DfbTypes.Single();

            var input = Assert.Single(type.Inputs);
            Assert.Equal("BOOL", input.TypeName);
        }

        [Fact]
        public void AnIlSection_KeepsItsText()
        {
            var section = Assert.Single(Import(new XefBuilder().Raw(@"<FBSource nameOfFBType=""Listing"">
  <FBProgram name=""Body""><ILSource>LD A
ST B</ILSource></FBProgram>
</FBSource>")).DfbTypes.Single().Sections);

            Assert.Equal("IL", section.Language);
            Assert.Null(section.Canvas);
            Assert.Equal("LD A" + (char)10 + "ST B", section.SourceText);
        }

        [Fact]
        public void AnFbProgramWithoutASource_IsNotListed()
        {
            var type = Import(new XefBuilder().Raw(@"<FBSource nameOfFBType=""Noted"">
  <FBProgram name=""Body""><comment>only a note</comment></FBProgram>
</FBSource>")).DfbTypes.Single();

            Assert.Empty(type.Sections);
        }

        [Fact]
        public void ARepeatedTypeName_IsListedTwice_AndWarnedAbout()
        {
            var result = Import(new XefBuilder().Raw(PlcDfbTests.AndDfb).Raw(PlcDfbTests.AndDfb));

            Assert.Equal(2, result.DfbTypes.Count);
            Assert.Contains(result.Warnings, w => w.Contains("MyAnd") && w.Contains("declared more than once") && w.Contains("first"));
        }

        [Fact]
        public void ATypeWithoutAName_IsListed_AndWarnedAbout()
        {
            var result = Import(new XefBuilder().Raw(@"<FBSource nameOfFBType=""""><inputParameters><variables name=""A"" typeName=""BOOL""/></inputParameters></FBSource>"));

            Assert.Single(result.DfbTypes);
            Assert.Contains(result.Warnings, w => w.Contains("no name"));
        }

        private static PlcXmlImportResult Import(XefBuilder xef)
        {
            var result = new PlcXmlImporter().ImportFromXml(xef.Build(), "test.xef");
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return result;
        }

        // A protected DFB is listed with its interface and no sections, whatever FBProgram its XML carries.
        private static void AssertProtectedKeepsNoSections(string program)
        {
            var locked = Import(new XefBuilder().Raw(ProtectedDfbWith(program))).DfbTypes.Single();

            Assert.True(locked.IsProtected);
            Assert.Equal(new[] { "A", "B" }, locked.Inputs.Select(v => v.Name));
            Assert.Equal(new[] { "Q" }, locked.Outputs.Select(v => v.Name));
            Assert.Empty(locked.Sections);
        }

        // An encrypted DFB ("crypted") with the interface A, B (in) and Q (out), followed by the given FBProgram.
        private static string ProtectedDfbWith(string program)
            => "<FBSource nameOfFBType=\"Locked\"><crypted>00</crypted><ExternalToolsOnly>" +
               "<inputParameters><variables name=\"A\" typeName=\"BOOL\"/><variables name=\"B\" typeName=\"BOOL\"/></inputParameters>" +
               "<outputParameters><variables name=\"Q\" typeName=\"BOOL\"/></outputParameters>" +
               "</ExternalToolsOnly>" + program + "</FBSource>";

        // A DFB with the interface A, B (in) and Q (out), and an FBD body of one network per argument.
        private static string FbdType(string typeName, params string[] networks)
            => $"<FBSource nameOfFBType=\"{typeName}\">" +
               "<inputParameters><variables name=\"A\" typeName=\"BOOL\"/><variables name=\"B\" typeName=\"BOOL\"/></inputParameters>" +
               "<outputParameters><variables name=\"Q\" typeName=\"BOOL\"/></outputParameters>" +
               "<FBProgram name=\"Body\"><FBDSource>" +
               string.Concat(networks.Select(n => $"<networkFBD>{n}</networkFBD>")) +
               "</FBDSource></FBProgram></FBSource>";
    }
}

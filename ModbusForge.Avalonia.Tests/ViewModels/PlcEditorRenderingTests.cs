using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Media;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    /// <summary>
    /// Imported links are drawn the way Control Expert draws them: from the source
    /// pin through the recorded bend points to the destination pin, as straight
    /// segments.
    /// </summary>
    /// <remarks>
    /// Geometry (20 px cells after a 60 px / 40 px margin; pins meet the frame half a
    /// cell in from the node edge): TON FBI_1 at cell (10, 2), 7 x 6 cells, so its Q
    /// output (data row 0) is at (60 + 16.5 * 20, 40 + 6.5 * 20) = (390, 170); MOVE .2
    /// at cell (30, 4) shows EN, so its IN (data row 0, grid row 8) is at
    /// (60 + 30.5 * 20, 40 + 8.5 * 20) = (670, 210); the bends at cells (20, 6) and
    /// (20, 8) are (470, 170) and (470, 210).
    /// </remarks>
    public sealed class PlcEditorRenderingTests
    {
        private const string LinkedXef = @"<FEFExchangeFile>
  <program>
    <identProgram name=""PUMP"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""FBI_1"" typeName=""TON"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""6"">
        <objPosition posX=""10"" posY=""2""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN""/>
          <inputVariable invertedPin=""false"" formalParameter=""PT"" effectiveParameter=""t#6s""/>
          <outputVariable invertedPin=""false"" formalParameter=""Q""/>
          <outputVariable invertedPin=""false"" formalParameter=""ET""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName="".2"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""true"" width=""7"" height=""5"">
        <objPosition posX=""30"" posY=""4""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""EN""/>
          <inputVariable invertedPin=""false"" formalParameter=""IN""/>
          <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName="".3"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""5"">
        <objPosition posX=""30"" posY=""20""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT""/>
        </descriptionFFB>
      </FFBBlock>
      <linkFB>
        <linkSource parentObjectName=""FBI_1"" pinName=""Q""><objPosition posX=""16"" posY=""6""/></linkSource>
        <linkDestination parentObjectName="".2"" pinName=""IN""><objPosition posX=""30"" posY=""8""/></linkDestination>
        <gridObjPosition posX=""20"" posY=""6""/>
        <gridObjPosition posX=""20"" posY=""8""/>
      </linkFB>
      <linkFB>
        <linkSource parentObjectName="".2"" pinName=""OUT""><objPosition posX=""36"" posY=""8""/></linkSource>
        <linkDestination parentObjectName="".3"" pinName=""IN""><objPosition posX=""30"" posY=""24""/></linkDestination>
      </linkFB>
    </networkFBD></FBDSource>
  </program>
</FEFExchangeFile>";

        private static PlcEditorViewModel LoadedEditor()
        {
            var result = new PlcXmlImporter().ImportFromXml(XDocument.Parse(LinkedXef), "linked.xef");
            var section = Assert.Single(result.Sections);
            var editor = new PlcEditorViewModel(NoopTagWindowService.Instance);
            editor.LoadImportedPrograms(new (string, List<VisualNode>, List<NodeConnection>)[]
            {
                (section.Name, section.Nodes, section.Connections)
            });
            return editor;
        }

        private static ConnectionLine LineInto(PlcEditorViewModel editor, string targetNodeSuffix)
            => editor.ConnectionLines.Single(l => l.TargetId.EndsWith(targetNodeSuffix, System.StringComparison.Ordinal));

        [Fact]
        public void ImportedLink_RunsFromPinToPinThroughItsControlExpertBends()
        {
            using var editor = LoadedEditor();

            var line = LineInto(editor, "_.2");

            Assert.Equal(
                new[] { (390.0, 170.0), (470.0, 170.0), (470.0, 210.0), (670.0, 210.0) },
                line.PathPoints.Select(p => (p.X, p.Y)).ToArray());
        }

        [Fact]
        public void NavigatorBlockCount_LeavesOutSectionTextBoxes()
        {
            var xml = LinkedXef.Replace("</networkFBD>",
                @"<textBox width=""12"" height=""3"">Note<objPosition posX=""0"" posY=""0""/></textBox></networkFBD>");
            var project = new PlcProjectViewModel();

            project.LoadProject(new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "linked.xef"));

            // Three FFBs (TON, two MOVEs); the text box is not a block.
            var program = Flatten(project.RootNodes).Single(n => n.Kind == PlcNodeKind.ProgramFbd);
            Assert.Equal("PUMP (3)", program.Title);
        }

        [Fact]
        public void SelectingAnStProgram_ShowsItsSourceInsteadOfACanvas()
        {
            var xml = LinkedXef.Replace("</FEFExchangeFile>", @"
  <program>
    <identProgram name=""PLC_STATUS"" type=""section"" task=""MAST""/>
    <STSource>PLC_Healthy := %S10;</STSource>
  </program>
</FEFExchangeFile>");
            var project = new PlcProjectViewModel();
            project.LoadProject(new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "st.xef"));

            project.SelectedNode = Flatten(project.RootNodes).Single(n => n.ProgramName == "PLC_STATUS");

            Assert.Equal(("PLC_Healthy := %S10;", true, false),
                (project.SelectedSourceText, project.HasSourceSelected, project.HasFbdSelected));
        }

        [Fact]
        public void SelectingDataManagement_ListsTheVariables_FilteredByText()
        {
            var xml = LinkedXef.Replace("<program>", @"<dataBlock>
    <variables name=""Pump_Run"" typeName=""EBOOL"" topologicalAddress=""%M10""><comment>Pump running</comment></variables>
    <variables name=""Pump_Speed"" typeName=""INT"" topologicalAddress=""%MW100""/>
    <variables name=""Valve_Open"" typeName=""EBOOL""/>
  </dataBlock>
  <program>");
            var project = new PlcProjectViewModel();
            project.LoadProject(new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "vars.xef"));

            project.SelectedNode = Flatten(project.RootNodes).Single(n => n.Kind == PlcNodeKind.DdtList);
            project.VariableFilter = "pump";

            Assert.True(project.HasVariablesSelected);
            Assert.Equal(new[] { "Pump_Run", "Pump_Speed" }, project.FilteredVariables.Select(v => v.Name).ToArray());
        }

        private static IEnumerable<PlcTreeNodeViewModel> Flatten(IEnumerable<PlcTreeNodeViewModel> nodes)
            => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

        [Fact]
        public void ImportedLink_WithoutBends_IsAStraightSegmentNotACurve()
        {
            using var editor = LoadedEditor();

            var line = LineInto(editor, "_.3");

            var figure = Assert.Single(((PathGeometry)line.PathData!).Figures!);
            Assert.IsNotType<BezierSegment>(Assert.Single(figure.Segments!));
        }

        private sealed class NoopTagWindowService : ITagWindowService
        {
            public static readonly NoopTagWindowService Instance = new();
            public void ShowTagBrowser() { }
            public void ShowWatchWindow() { }
        }
    }
}

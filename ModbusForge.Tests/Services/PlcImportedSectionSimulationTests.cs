using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using ModbusForge.Data;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Running an imported Unity section: blocks without a simulation equivalent and
    /// section text boxes load into the engine but never produce values of their own.
    /// </summary>
    public class PlcImportedSectionSimulationTests
    {
        // A TON, an unsupported MOVE and a text box (the importer's Control Expert sample shape).
        private const string MixedSectionXef = @"<FEFExchangeFile>
  <dataBlock><variables name=""Pump_Run"" typeName=""EBOOL"" topologicalAddress=""%M10""/></dataBlock>
  <program>
    <identProgram name=""PUMP"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""FBI_1"" typeName=""TON"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""6"">
        <objPosition posX=""10"" posY=""2""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN"" effectiveParameter=""Pump_Run""/>
          <inputVariable invertedPin=""false"" formalParameter=""PT"" effectiveParameter=""t#6s""/>
          <outputVariable invertedPin=""false"" formalParameter=""Q""/>
          <outputVariable invertedPin=""false"" formalParameter=""ET""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName="".2"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""5"">
        <objPosition posX=""30"" posY=""2""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN""/>
          <outputVariable invertedPin=""false"" formalParameter=""OUT""/>
        </descriptionFFB>
      </FFBBlock>
      <linkFB>
        <linkSource parentObjectName=""FBI_1"" pinName=""Q""/>
        <linkDestination parentObjectName="".2"" pinName=""IN""/>
      </linkFB>
      <textBox width=""12"" height=""3"">Pump start delay<objPosition posX=""10"" posY=""20""/></textBox>
    </networkFBD></FBDSource>
  </program>
</FEFExchangeFile>";

        // An unsupported MOVE wired into an ADD whose OUT pin is bound to %MW10.
        private const string MoveIntoAddXef = @"<FEFExchangeFile>
  <dataBlock>
    <variables name=""Speed_SP"" typeName=""INT"" topologicalAddress=""%MW5""/>
    <variables name=""Speed_Out"" typeName=""INT"" topologicalAddress=""%MW10""/>
  </dataBlock>
  <program>
    <identProgram name=""Copy"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""MOVE1"" typeName=""MOVE"" width=""7"" height=""5"">
        <objPosition posX=""10"" posY=""2""/>
        <descriptionFFB>
          <inputVariable formalParameter=""IN"" effectiveParameter=""Speed_SP""/>
          <outputVariable formalParameter=""OUT""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName=""ADD1"" typeName=""ADD"" width=""7"" height=""5"">
        <objPosition posX=""30"" posY=""2""/>
        <descriptionFFB>
          <inputVariable formalParameter=""IN1""/>
          <inputVariable formalParameter=""IN2"" effectiveParameter=""0""/>
          <outputVariable formalParameter=""OUT"" effectiveParameter=""Speed_Out""/>
        </descriptionFFB>
      </FFBBlock>
      <linkFB>
        <linkSource parentObjectName=""MOVE1"" pinName=""OUT""/>
        <linkDestination parentObjectName=""ADD1"" pinName=""IN1""/>
      </linkFB>
    </networkFBD></FBDSource>
  </program>
</FEFExchangeFile>";

        private static PlcXmlSection Import(string xml)
        {
            var result = new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "section.xef");
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return Assert.Single(result.Sections);
        }

        private static VisualNodeEditorConfig ConfigFor(PlcXmlSection section) => new()
        {
            Nodes = new ObservableCollection<VisualNode>(section.Nodes),
            Connections = new ObservableCollection<NodeConnection>(section.Connections)
        };

        [Fact]
        public void ImportedSection_WithUnsupportedBlocksAndTextBoxes_RunsWithoutErrors()
        {
            var section = Import(MixedSectionXef);
            using var service = new AvaloniaVisualSimulationService();

            service.Start(ConfigFor(section));
            for (var i = 0; i < 3; i++)
            {
                service.UpdateNodeValues();
            }
            service.Stop();

            Assert.All(section.Nodes, n => Assert.Null(n.ErrorText));
        }

        [Fact]
        public void UnsupportedBlock_DoesNotInventAChangingSignal()
        {
            var section = Import(MoveIntoAddXef);
            using var service = new AvaloniaVisualSimulationService();
            var store = OfflineStore(service);

            service.Start(ConfigFor(section));
            var written = new HashSet<ushort>();
            for (var i = 0; i < 5; i++)
            {
                service.UpdateNodeValues();
                written.Add(store.HoldingRegisters[10]);
                Thread.Sleep(30);
            }
            service.Stop();

            // Nothing in the program changes, so its output must not change either:
            // an unsupported block used to run as a ramp Signal Generator here.
            Assert.Single(written);
        }

        private static DataStore OfflineStore(AvaloniaVisualSimulationService service)
            => (DataStore)typeof(AvaloniaVisualSimulationService).BaseType!
                .GetField("_dataStore", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(service)!;
    }
}

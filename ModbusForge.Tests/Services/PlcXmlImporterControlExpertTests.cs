using System.Linq;
using System.Xml.Linq;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// The imported FBD must carry what Control Expert draws for each block: the
    /// Unity type and instance name, every formal pin in its row with the actual
    /// parameter (variable or literal) attached, negated pins, the recorded link
    /// routes and the section's text boxes, on the exact Control Expert grid.
    /// </summary>
    /// <remarks>
    /// Grid geometry, measured on 20,843 link endpoints of the 23-file corpus:
    /// input pins sit in the block's first column, output pins in its last, data
    /// pin k in row y + 4 + k and EN/ENO in row y + 3. One grid cell renders as
    /// 20 px after a 60 px / 40 px section margin, so cell (c, r) has its centre at
    /// (60 + (c + 0.5) * 20, 40 + (r + 0.5) * 20).
    /// </remarks>
    public class PlcXmlImporterControlExpertTests
    {
        private const string SampleXef = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FEFExchangeFile>
  <dataBlock>
    <variables name=""Pump_Run"" typeName=""EBOOL"" topologicalAddress=""%M10""/>
    <variables name=""Speed"" typeName=""INT"" topologicalAddress=""%MW100""/>
  </dataBlock>
  <program>
    <identProgram name=""PUMP"" type=""section"" task=""MAST""/>
    <FBDSource nbRows=""24"" nbColumns=""36"">
      <networkFBD>
        <FFBBlock instanceName=""FBI_1"" typeName=""TON"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""6"">
          <objPosition posX=""10"" posY=""2""/>
          <descriptionFFB execAfter="""">
            <inputVariable invertedPin=""false"" formalParameter=""EN""/>
            <inputVariable invertedPin=""true"" formalParameter=""IN"" effectiveParameter=""Pump_Run""/>
            <inputVariable invertedPin=""false"" formalParameter=""PT"" effectiveParameter=""t#6s""/>
            <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
            <outputVariable invertedPin=""false"" formalParameter=""Q""/>
            <outputVariable invertedPin=""false"" formalParameter=""ET""/>
          </descriptionFFB>
          <comment>Run delay</comment>
        </FFBBlock>
        <FFBBlock instanceName="".2"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""true"" width=""7"" height=""5"">
          <objPosition posX=""30"" posY=""4""/>
          <descriptionFFB execAfter="""">
            <inputVariable invertedPin=""false"" formalParameter=""EN""/>
            <inputVariable invertedPin=""false"" formalParameter=""IN""/>
            <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
            <outputVariable invertedPin=""false"" formalParameter=""OUT"" effectiveParameter=""Speed""/>
          </descriptionFFB>
        </FFBBlock>
        <linkFB>
          <linkSource parentObjectName=""FBI_1"" pinName=""Q""><objPosition posX=""16"" posY=""6""/></linkSource>
          <linkDestination parentObjectName="".2"" pinName=""IN""><objPosition posX=""30"" posY=""8""/></linkDestination>
          <gridObjPosition posX=""20"" posY=""6""/>
          <gridObjPosition posX=""20"" posY=""8""/>
        </linkFB>
        <textBox width=""12"" height=""3"">Pump start delay<objPosition posX=""10"" posY=""20""/></textBox>
      </networkFBD>
    </FBDSource>
  </program>
</FEFExchangeFile>";

        private static PlcXmlSection Import(string xml = SampleXef)
        {
            var result = new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "sample.xef");
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return Assert.Single(result.Sections);
        }

        private static VisualNode Block(PlcXmlSection section, string typeName)
            => section.Nodes.Single(n => n.Plc?.TypeName == typeName);

        [Fact]
        public void Block_KeepsItsUnityTypeInstanceNameAndComment()
        {
            var ton = Block(Import(), "TON");

            Assert.Equal("FBI_1", ton.Plc!.InstanceName);
            Assert.Equal("Run delay", ton.Plc.Comment);
        }

        [Fact]
        public void ElementaryFunction_HasNoDisplayedInstanceName()
        {
            // Unity auto-names EFs ".1", ".2", ...; Control Expert shows no instance
            // name for them (only function blocks have instances).
            var move = Block(Import(), "MOVE");

            Assert.Null(move.Plc!.InstanceName);
        }

        [Fact]
        public void Block_SitsOnTheControlExpertGrid()
        {
            var ton = Block(Import(), "TON");

            Assert.Equal((260.0, 80.0, 140.0, 120.0), (ton.X, ton.Y, ton.Width, ton.Height));
        }

        [Fact]
        public void Pins_ListEveryFormalParameterInItsRow_HidingEnEnoWhenDisabled()
        {
            var pins = Block(Import(), "TON").Plc!.Pins;

            Assert.Equal(
                new[] { ("IN", true, 90.0), ("PT", true, 110.0), ("Q", false, 90.0), ("ET", false, 110.0) },
                pins.Select(p => (p.Name, p.IsInput, p.CenterY)).ToArray());
        }

        [Fact]
        public void Pins_ShowEnEnoAboveTheDataPinsWhenEnabled()
        {
            var pins = Block(Import(), "MOVE").Plc!.Pins;

            Assert.Equal(
                new[] { ("EN", true, 70.0), ("IN", true, 90.0), ("ENO", false, 70.0), ("OUT", false, 90.0) },
                pins.Select(p => (p.Name, p.IsInput, p.CenterY)).ToArray());
        }

        [Fact]
        public void EnablePin_IsDrawnWhenALinkAttachesToIt_EvenWithEnEnoOff()
        {
            // Real case (HCPLC001 ALARMS_1_B100): a SET, whose only input is EN, has
            // enEnO="false" yet a TON output is linked to its EN in row y + 3.
            var xml = SampleXef
                .Replace(@"enEnO=""true"" width=""7"" height=""5""", @"enEnO=""false"" width=""7"" height=""5""")
                .Replace(@"pinName=""IN""><objPosition posX=""30"" posY=""8""/>", @"pinName=""EN""><objPosition posX=""30"" posY=""7""/>");

            var pins = Block(Import(xml), "MOVE").Plc!.Pins;

            Assert.Equal(
                new[] { ("EN", true, 70.0), ("IN", true, 90.0), ("OUT", false, 90.0) },
                pins.Select(p => (p.Name, p.IsInput, p.CenterY)).ToArray());
        }

        [Fact]
        public void PinRows_FollowTheTypeSignaturePositionPins()
        {
            // Real case (LCPLC001 Analogs_IN): I_SCALE_WARN declares Y at PositionPin 2
            // and WARN at 3, so Y is drawn in the block's second data row (the link
            // from FBI_14.Y leaves at row y + 5), not the first.
            const string xml = @"<FEFExchangeFile>
  <EFBSource nameOfEFBType=""I_SCALE_WARN"">
    <ExternalToolsOnly>
      <inputParameters>
        <variables name=""CHANNEL"" typeName=""ANL_IN""><attribute name=""PositionPin"" value=""1""/></variables>
        <variables name=""MN"" typeName=""REAL""><attribute name=""PositionPin"" value=""2""/></variables>
        <variables name=""MX"" typeName=""REAL""><attribute name=""PositionPin"" value=""3""/></variables>
      </inputParameters>
      <outputParameters>
        <variables name=""Y"" typeName=""REAL""><attribute name=""PositionPin"" value=""2""/></variables>
        <variables name=""WARN"" typeName=""BOOL""><attribute name=""PositionPin"" value=""3""/></variables>
      </outputParameters>
    </ExternalToolsOnly>
  </EFBSource>
  <program>
    <identProgram name=""Analogs_IN"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""FBI_14"" typeName=""I_SCALE_WARN"" additionnalPinNumber=""0"" enEnO=""false"" width=""10"" height=""7"">
        <objPosition posX=""14"" posY=""3""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""EN""/>
          <inputVariable invertedPin=""false"" formalParameter=""CHANNEL"" effectiveParameter=""KCLC_PLC001_AI0001_iChanel1""/>
          <inputVariable invertedPin=""false"" formalParameter=""MN"" effectiveParameter=""0.0""/>
          <inputVariable invertedPin=""false"" formalParameter=""MX"" effectiveParameter=""4095.0""/>
          <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
          <outputVariable invertedPin=""false"" formalParameter=""Y""/>
          <outputVariable invertedPin=""false"" formalParameter=""WARN"" effectiveParameter=""KCLC_XXX001_PT0001_sBusHthy""/>
        </descriptionFFB>
      </FFBBlock>
    </networkFBD></FBDSource>
  </program>
</FEFExchangeFile>";

            var pins = Block(Import(xml), "I_SCALE_WARN").Plc!.Pins;

            Assert.Equal(
                new[] { ("CHANNEL", 90.0), ("MN", 110.0), ("MX", 130.0), ("Y", 110.0), ("WARN", 130.0) },
                pins.Select(p => (p.Name, p.CenterY)).ToArray());
        }

        [Fact]
        public void Link_ToADuplicatedInstanceName_AttachesToTheBlockAtItsRecordedEndpoint()
        {
            // Real case (HCPLC002 HC_TMT_02, SK3304 MISC_CONTROL): several EFs in one
            // network share an auto name like ".120". The link's recorded endpoint
            // cell tells them apart; here it is the IN pin of the upper MOVE.
            var xml = SampleXef.Replace("</networkFBD>", @"
        <FFBBlock instanceName="".2"" typeName=""MOVE"" additionnalPinNumber=""0"" enEnO=""true"" width=""7"" height=""5"">
          <objPosition posX=""30"" posY=""40""/>
          <descriptionFFB execAfter="""">
            <inputVariable invertedPin=""false"" formalParameter=""EN""/>
            <inputVariable invertedPin=""false"" formalParameter=""IN""/>
            <outputVariable invertedPin=""false"" formalParameter=""ENO""/>
            <outputVariable invertedPin=""false"" formalParameter=""OUT""/>
          </descriptionFFB>
        </FFBBlock>
      </networkFBD>");
            var section = Import(xml);

            var link = Assert.Single(section.Connections);
            var target = section.Nodes.Single(n => n.Id == link.TargetNodeId);

            Assert.Equal(120.0, target.Y); // the MOVE at cell row 4, not the one at row 40
        }

        [Fact]
        public void LinkedPin_IsDrawnWhereControlExpertRecordedTheLinkEndpoint()
        {
            // Real case (HEPLC003_PriorShutdown, an export without type signatures):
            // an R_TRIG's CLK link is recorded in row y + 3, one above the row the
            // listing order implies. The recorded endpoint is what Control Expert drew.
            const string xml = @"<FEFExchangeFile>
  <program>
    <identProgram name=""EDGES"" type=""section"" task=""MAST""/>
    <FBDSource><networkFBD>
      <FFBBlock instanceName=""FBI_1"" typeName=""TON"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""6"">
        <objPosition posX=""2"" posY=""0""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""IN""/>
          <inputVariable invertedPin=""false"" formalParameter=""PT""/>
          <outputVariable invertedPin=""false"" formalParameter=""Q""/>
          <outputVariable invertedPin=""false"" formalParameter=""ET""/>
        </descriptionFFB>
      </FFBBlock>
      <FFBBlock instanceName=""FBI_597"" typeName=""R_TRIG"" additionnalPinNumber=""0"" enEnO=""false"" width=""7"" height=""5"">
        <objPosition posX=""20"" posY=""1""/>
        <descriptionFFB execAfter="""">
          <inputVariable invertedPin=""false"" formalParameter=""CLK""/>
          <outputVariable invertedPin=""false"" formalParameter=""Q""/>
        </descriptionFFB>
      </FFBBlock>
      <linkFB>
        <linkSource parentObjectName=""FBI_1"" pinName=""Q""><objPosition posX=""8"" posY=""4""/></linkSource>
        <linkDestination parentObjectName=""FBI_597"" pinName=""CLK""><objPosition posX=""20"" posY=""4""/></linkDestination>
      </linkFB>
    </networkFBD></FBDSource>
  </program>
</FEFExchangeFile>";

            var clk = Block(Import(xml), "R_TRIG").Plc!.Pins.Single(p => p.Name == "CLK");

            // Row 4 of the section is row 3 of the block (posY 1): (3 + 0.5) * 20.
            Assert.Equal(70.0, clk.CenterY);
        }

        [Fact]
        public void ZefExport_IsReadFromTheXefInsideTheZip()
        {
            // A .ZEF is a zip holding the project's .XEF (Schneider FAQ000259996 /
            // FA241538), next to other content such as DTM configuration.
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"export-{System.Guid.NewGuid():N}.ZEF");
            try
            {
                using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
                {
                    using (var dtm = new System.IO.StreamWriter(zip.CreateEntry("DTM/config.xml").Open()))
                        dtm.Write("<dtm/>");
                    using (var xef = new System.IO.StreamWriter(zip.CreateEntry("PROJECT.XEF").Open()))
                        xef.Write(SampleXef);
                }

                var result = new PlcXmlImporter().Import(path);

                Assert.True(result.Success, string.Join("; ", result.Errors));
                Assert.Equal("PUMP", Assert.Single(result.Sections).Name);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void Variables_KeepWhatTheDataEditorShows()
        {
            // Control Expert's Data Editor lists each variable's name, type, address,
            // initial value and comment (corpus shape: GGPLC001 dataBlock).
            const string xml = @"<FEFExchangeFile>
  <dataBlock>
    <variables name=""KCGE_CCD001_PY1321_sPV"" typeName=""REAL"" topologicalAddress=""%MW57001"">
      <comment>GECCD01 PT1321 HEAD PV</comment>
      <attribute name=""Save"" value=""-1"" />
      <variableInit value=""3.783178"" />
    </variables>
    <variables name=""Pump_Run"" typeName=""EBOOL""/>
  </dataBlock>
</FEFExchangeFile>";

            var result = new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "vars.xef");

            Assert.Equal(
                new[]
                {
                    ("KCGE_CCD001_PY1321_sPV", "REAL", "%MW57001", "3.783178", "GECCD01 PT1321 HEAD PV"),
                    ("Pump_Run", "EBOOL", (string?)null, (string?)null, (string?)null)
                },
                result.Variables.Select(v => (v.Name, v.TypeName, v.Address, v.InitialValue, v.Comment)).ToArray());
        }

        [Fact]
        public void StSection_KeepsItsSourceText()
        {
            // Control Expert shows ST sections as text; the corpus has 39 of them.
            const string xml = @"<FEFExchangeFile>
  <program>
    <identProgram name=""PLC_STATUS"" type=""section"" task=""MAST""/>
    <STSource>(* Copy status bits *)
PLC_Healthy := %S10;
if Reset then
	%S50 := 0;
end_if;
</STSource>
  </program>
</FEFExchangeFile>";

            var result = new PlcXmlImporter().ImportFromXml(XDocument.Parse(xml), "st.xef");

            var program = Assert.Single(result.Programs);
            Assert.Equal("(* Copy status bits *)\nPLC_Healthy := %S10;\nif Reset then\n\t%S50 := 0;\nend_if;", program.SourceText);
        }

        [Fact]
        public void Pins_CarryTheActualParameterAndNegation()
        {
            var pins = Block(Import(), "TON").Plc!.Pins;

            Assert.Equal(
                new[] { ("IN", "Pump_Run", true), ("PT", "t#6s", false), ("Q", null, false), ("ET", null, false) },
                pins.Select(p => (p.Name, p.ActualParameter, p.Inverted)).ToArray());
        }

        [Fact]
        public void UnsupportedBlock_IsAnInertPlcBlockNotASignalGenerator()
        {
            var move = Block(Import(), "MOVE");

            Assert.Equal(PlcElementType.PlcBlock, move.ElementType);
        }

        [Fact]
        public void Link_KeepsItsUnityPinsAndControlExpertRoute()
        {
            var link = Assert.Single(Import().Connections);

            Assert.Equal(("Q", "IN"), (link.SourcePin, link.TargetPin));
            Assert.Equal(
                new[] { (470.0, 170.0), (470.0, 210.0) },
                link.RoutePoints.Select(p => (p.X, p.Y)).ToArray());
        }

        [Fact]
        public void TextBox_BecomesACommentOnTheGrid()
        {
            var comment = Import().Nodes.Single(n => n.ElementType == PlcElementType.PlcComment);

            Assert.Equal("Pump start delay", comment.Plc!.Text);
            Assert.Equal((260.0, 440.0, 240.0, 60.0), (comment.X, comment.Y, comment.Width, comment.Height));
        }

        [Fact]
        public void BlockInTheTopLeftCell_StaysThere()
        {
            // (0, 0) is a real Control Expert cell, not "no position".
            var xml = SampleXef.Replace(@"posX=""10"" posY=""2""", @"posX=""0"" posY=""0""");

            var ton = Block(Import(xml), "TON");

            Assert.Equal((60.0, 40.0), (ton.X, ton.Y));
        }

        [Fact]
        public void LongSection_KeepsTheExactGridInsteadOfBeingCompressed()
        {
            // Corpus sections reach 1,072 rows (21,440 px); squeezing them breaks the
            // pin and link alignment, so the canvas grows instead.
            var xml = SampleXef.Replace(@"posX=""30"" posY=""4""", @"posX=""30"" posY=""1000""");

            var move = Block(Import(xml), "MOVE");

            Assert.Equal(40.0 + 1000 * 20, move.Y);
        }
    }
}

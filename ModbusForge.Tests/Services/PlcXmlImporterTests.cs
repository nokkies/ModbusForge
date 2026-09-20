using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="PlcXmlImporter"/> — the Schneider Unity Pro FEF
    /// importer that turns .XEF project files into a runnable ModbusForge
    /// simulation graph.
    /// </summary>
    public class PlcXmlImporterTests
    {
        // ----------------------------------------------------------------
        // Address parsing
        // ----------------------------------------------------------------

        [Theory]
        [InlineData("%MW2002", PlcArea.HoldingRegister, 2002)]
        [InlineData("MW2002", PlcArea.HoldingRegister, 2002)]
        [InlineData("%IW1000", PlcArea.InputRegister, 1000)]
        [InlineData("%IB0.2", PlcArea.DiscreteInput, 2)]   // word 0, bit 2 → address 0*16+2
        [InlineData("%QB3.5", PlcArea.Coil, 53)]           // word 3, bit 5 → 3*16+5

        public void TryParseTopologicalAddress_ParsesAllKnownFormats(string address, PlcArea expectedArea, int expectedAddr)
        {
            var reference = PlcXmlImporter.TryParseTopologicalAddress(address);

            Assert.NotNull(reference);
            Assert.Equal(expectedArea, reference!.Area);
            Assert.Equal(expectedAddr, reference.Address);
            Assert.Equal(address, reference.SymbolicName);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not an address")]
        [InlineData("%X99")]
        public void TryParseTopologicalAddress_ReturnsNullForUnknown(string? address)
        {
            Assert.Null(PlcXmlImporter.TryParseTopologicalAddress(address!));
        }

        // ----------------------------------------------------------------
        // Type mapping
        // ----------------------------------------------------------------

        [Theory]
        [InlineData("AND", PlcElementType.AND)]
        [InlineData("OR", PlcElementType.OR)]
        [InlineData("NOT", PlcElementType.NOT)]
        [InlineData("RS", PlcElementType.RS)]
        [InlineData("TON", PlcElementType.TON)]
        [InlineData("CTU", PlcElementType.CTU)]
        [InlineData("EQ", PlcElementType.COMPARE_EQ)]
        [InlineData("GT_REAL", PlcElementType.COMPARE_GT_REAL)]
        [InlineData("ADD_REAL", PlcElementType.MATH_ADD_REAL)]
        public void TypeMapping_MapsKnownUnityTypes(string unityType, PlcElementType expected)
        {
            Assert.True(PlcXmlImporter.TypeMapping.TryGetValue(unityType, out var mapped));
            Assert.Equal(expected, mapped);
        }

        [Theory]
        [InlineData("MOVE")]
        [InlineData("I_SCALE_WARN")]
        [InlineData("xPID101")]
        [InlineData("xSEQStep")]
        [InlineData("COMPLETELY_UNKNOWN_FB")]
        public void TypeMapping_MapsOpaqueTypesToNullOrAbsent(string unityType)
        {
            if (PlcXmlImporter.TypeMapping.TryGetValue(unityType, out var mapped))
                Assert.Null(mapped);
        }

        // ----------------------------------------------------------------
        // End-to-end: small inline XEF sample
        // ----------------------------------------------------------------

        private const string SampleXef = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<FEFExchangeFile>
  <fileHeader company=""Schneider Automation"" product=""Unity Pro XL""/>
  <contentHeader name=""SamplePLC"" version=""0.0.1""/>
  <dataBlock>
    <variables name=""Pump_Start"" typeName=""BOOL"" topologicalAddress=""%QB0.0""/>
    <variables name=""Pump_Run"" typeName=""BOOL"" topologicalAddress=""%QB0.1""/>
    <variables name=""Stop_Push"" typeName=""BOOL"" topologicalAddress=""%IB0.0""/>
    <variables name=""Pump_Fault"" typeName=""BOOL"" topologicalAddress=""%IB0.1""/>
    <variables name=""Pump_Speed"" typeName=""REAL"" topologicalAddress=""%MW100""/>
  </dataBlock>
  <program>
    <identProgram name=""PumpControl"" type=""section"" task=""MAST""/>
    <FBDSource nbRows=""10"" nbColumns=""5"">
      <networkFBD>
        <FFBBlock instanceName=""AND1"" typeName=""AND"" width=""7"" height=""5"">
          <objPosition posX=""10"" posY=""2""/>
          <descriptionFFB>
            <inputVariable formalParameter=""IN1"" effectiveParameter=""Pump_Start""/>
            <inputVariable formalParameter=""IN2"" effectiveParameter=""Pump_Run""/>
            <outputVariable formalParameter=""OUT"" effectiveParameter=""Pump_Fault""/>
          </descriptionFFB>
        </FFBBlock>
        <FFBBlock instanceName=""MOVE1"" typeName=""MOVE"" width=""7"" height=""5"">
          <objPosition posX=""30"" posY=""2""/>
          <descriptionFFB>
            <inputVariable formalParameter=""IN"" effectiveParameter=""Pump_Speed""/>
            <outputVariable formalParameter=""OUT"" effectiveParameter=""Stop_Push""/>
          </descriptionFFB>
        </FFBBlock>
        <linkFB>
          <linkSource parentObjectName=""AND1"" pinName=""OUT""/>
          <linkDestination parentObjectName=""MOVE1"" pinName=""IN""/>
        </linkFB>
      </networkFBD>
    </FBDSource>
  </program>
</FEFExchangeFile>";

        private static PlcXmlImportResult ImportSample()
        {
            var xef = XDocument.Parse(SampleXef);
            return new PlcXmlImporter().ImportFromXml(xef, "SampleXef");
        }

        [Fact]
        public void ImportSample_ParsesTags()
        {
            var result = ImportSample();

            Assert.True(result.Success);
            Assert.Equal(5, result.TagCount);
        }

        [Fact]
        public void ImportSample_ParsesSectionWithNodesAndConnections()
        {
            var result = ImportSample();

            var section = Assert.Single(result.Sections);
            Assert.Equal("PumpControl", section.Name);
            Assert.Equal("MAST", section.Task);
            Assert.Equal(2, section.Nodes.Count);
            Assert.Single(section.Connections);
        }

        [Fact]
        public void ImportSample_MapsAndBlockToNativeType()
        {
            var result = ImportSample();
            var section = result.Sections.Single();

            var andNode = section.Nodes.Single(n => n.Name == "AND1");
            Assert.Equal(PlcElementType.AND, andNode.ElementType);
            // AND1's IN1/IN2/OUT are bound to the addressed tags.
            Assert.NotNull(andNode.Input1Address);
            Assert.NotNull(andNode.Input2Address);
            Assert.NotNull(andNode.OutputAddress);
        }

        [Fact]
        public void ImportSample_MapsMoveToOpaqueWithTagBindings()
        {
            var result = ImportSample();
            var section = result.Sections.Single();

            var moveNode = section.Nodes.Single(n => n.Name.StartsWith("MOVE1"));
            Assert.Equal(PlcXmlImporter.Opaque, moveNode.ElementType);
            // Opaque node carries the type name in its display name.
            Assert.Contains("MOVE", moveNode.Name);
            // MOVE's IN pin is bound to Pump_Speed (an addressed tag).
            Assert.NotNull(moveNode.Input1Address);
        }

        [Fact]
        public void ImportSample_ConnectionWiresAndToMove()
        {
            var result = ImportSample();
            var section = result.Sections.Single();

            var conn = Assert.Single(section.Connections);
            // Node ids are "S_<instance>" — verify they're non-empty and distinct.
            Assert.False(string.IsNullOrEmpty(conn.SourceNodeId));
            Assert.False(string.IsNullOrEmpty(conn.TargetNodeId));
            Assert.NotEqual(conn.SourceNodeId, conn.TargetNodeId);
        }

        [Fact]
        public void ImportSample_ReportsOpaqueTypes()
        {
            var result = ImportSample();

            Assert.Contains("MOVE", result.UsedOpaqueTypes);
        }

        [Fact]
        public void ImportSample_RejectsNonFefDocument()
        {
            var xef = XDocument.Parse(@"<?xml version=""1.0""?><root><x/></root>");
            var result = new PlcXmlImporter().ImportFromXml(xef, "NotFef");

            Assert.False(result.Success);
            Assert.Single(result.Errors);
        }

        // ----------------------------------------------------------------
        // File-based: import a real .XEF from the resources folder (if present)
        // ----------------------------------------------------------------

        [Fact]
        public void Import_RealLcplc001_IfAvailable()
        {
            var candidates = new[]
            {
                @"C:\Users\rvn\Documents\GitHub\UnityParseEngine\resources\XML\LCPLC001.XEF",
                @"C:\Users\rvn\Documents\GitHub\UnityParseEngine\resources\XML\GGPLC001.XEF"
            };
            var path = candidates.FirstOrDefault(File.Exists);
            if (path == null)
            {
                // Skip silently when the sample is not on this machine.
                return;
            }

            var result = new PlcXmlImporter().Import(path);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.True(result.TagCount > 100, $"Expected >100 tags, got {result.TagCount}");
            Assert.True(result.SectionsFound > 0, "Expected at least one section");
            Assert.True(result.TotalNodes > 0, "Expected at least one node");
            Assert.True(result.TotalConnections > 0, "Expected at least one connection");
        }
    }
}

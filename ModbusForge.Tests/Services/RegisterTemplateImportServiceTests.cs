using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Tests.Services
{
    public class RegisterTemplateImportServiceTests
    {
        private static RegisterTemplateImportResult ImportCsv(string csv, AddressingConvention addressing = AddressingConvention.ZeroBased)
        {
            var service = new RegisterTemplateImportService();
            using var reader = new StringReader(csv);
            return service.ImportCsv(reader, addressing);
        }

        [Fact]
        public void ImportCsv_ParsesAllSupportedColumns()
        {
            var result = ImportCsv(
                "TagName,Description,Group,RegisterType,Address,Bit,DataType,WordOrder,Length,Scale,Offset,Unit,Access,Enum,Default,Range\n" +
                "Pump1_Speed,Speed feedback,Pumps,HoldingRegister,100,3,UInt16,CDAB,2,0.1,5,Hz,r,0=Off;1=On,7,5..50\n");

            Assert.Empty(result.Errors);
            var entry = Assert.Single(result.Entries);
            Assert.Equal("Pump1_Speed", entry.TagName);
            Assert.Equal("Speed feedback", entry.Description);
            Assert.Equal("Pumps", entry.Group);
            Assert.Equal(PlcArea.HoldingRegister, entry.RegisterType);
            Assert.Equal(100, entry.Address);
            Assert.Equal(3, entry.Bit);
            Assert.Equal(TagDataType.UInt16, entry.DataType);
            Assert.Equal(WordOrder.LittleEndian, entry.WordOrder);
            Assert.Equal(2, entry.Length);
            Assert.Equal(0.1, entry.Scale);
            Assert.Equal(5, entry.Offset);
            Assert.Equal("Hz", entry.Unit);
            Assert.Equal(RegisterAccess.ReadOnly, entry.Access);
            Assert.Equal("Off", entry.Enum[0]);
            Assert.Equal("On", entry.Enum[1]);
            Assert.Equal(7, entry.Default);
            Assert.Equal(5, entry.RangeMin);
            Assert.Equal(50, entry.RangeMax);
            Assert.Equal(2, entry.SourceRow);
        }

        [Theory]
        [InlineData("HR", PlcArea.HoldingRegister)]
        [InlineData("input register", PlcArea.InputRegister)]
        [InlineData("Coil", PlcArea.Coil)]
        [InlineData("discrete-input", PlcArea.DiscreteInput)]
        public void ImportCsv_AcceptsAreaAliases(string area, PlcArea expected)
        {
            var result = ImportCsv($"Tag Name,Register Type,Register\nT1,{area},7\n");

            Assert.Empty(result.Errors);
            Assert.Equal(expected, Assert.Single(result.Entries).RegisterType);
        }

        [Theory]
        [InlineData(AddressingConvention.ZeroBased, "40", 40, PlcArea.HoldingRegister)]
        [InlineData(AddressingConvention.OneBased, "40", 39, PlcArea.HoldingRegister)]
        [InlineData(AddressingConvention.Modicon, "40001", 0, PlcArea.HoldingRegister)]
        [InlineData(AddressingConvention.Modicon, "30011", 10, PlcArea.InputRegister)]
        [InlineData(AddressingConvention.Modicon, "10005", 4, PlcArea.DiscreteInput)]
        [InlineData(AddressingConvention.Modicon, "400101", 100, PlcArea.HoldingRegister)]
        public void ImportCsv_AppliesAddressingConvention(AddressingConvention addressing, string address, int expected, PlcArea expectedArea)
        {
            var result = ImportCsv($"TagName,Address\nT1,{address}\n", addressing);

            Assert.Empty(result.Errors);
            var entry = Assert.Single(result.Entries);
            Assert.Equal(expected, entry.Address);
            Assert.Equal(expectedArea, entry.RegisterType);
            Assert.Equal(address, entry.RawAddress);
        }

        [Fact]
        public void ImportCsv_RejectsInvalidModiconAddress()
        {
            var result = ImportCsv("TagName,Address\nT1,20001\n", AddressingConvention.Modicon);

            Assert.Empty(result.Entries);
            Assert.Contains(result.Errors, e => e.Column == "Address" && e.Message.Contains("Modicon"));
        }

        [Fact]
        public void ImportCsv_DefaultsDataTypeAndLengthPerArea()
        {
            var result = ImportCsv("TagName,Area,Address,DataType\nRunning,Coil,3,\nLevel,Holding,4,Float\n");

            Assert.Equal(TagDataType.Bool, result.Entries[0].DataType);
            Assert.Equal(1, result.Entries[0].Length);
            Assert.Equal(TagDataType.Float, result.Entries[1].DataType);
            Assert.Equal(2, result.Entries[1].Length);
        }

        [Fact]
        public void ImportCsv_HandlesQuotedFieldsAndSemicolonDelimiter()
        {
            var result = ImportCsv("TagName;Description;Address\nT1;\"Level, tank 1\";12\n");

            var entry = Assert.Single(result.Entries);
            Assert.Equal("Level, tank 1", entry.Description);
            Assert.Equal(12, entry.Address);
        }

        [Fact]
        public void ImportCsv_RejectsInvalidRowsButKeepsValidOnes()
        {
            var result = ImportCsv(
                "TagName,Area,Address,Bit\n" +
                "Good,HoldingRegister,1,\n" +
                ",HoldingRegister,2,\n" +
                "BadAddress,HoldingRegister,abc,\n" +
                "OutOfRange,HoldingRegister,70000,\n" +
                "BadArea,Nonsense,4,\n" +
                "BadBit,HoldingRegister,5,42\n" +
                "Good,HoldingRegister,6,\n");

            Assert.Equal(7, result.RowsRead);
            Assert.Equal("Good", Assert.Single(result.Entries).TagName);
            Assert.Equal(6, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.Column == "TagName" && e.Message.Contains("required"));
            Assert.Contains(result.Errors, e => e.Column == "TagName" && e.Message.Contains("Duplicate"));
            Assert.Contains(result.Errors, e => e.Column == "Address" && e.Message.Contains("valid address"));
            Assert.Contains(result.Errors, e => e.Column == "Address" && e.Message.Contains("outside the valid range"));
            Assert.Contains(result.Errors, e => e.Column == "RegisterType");
            Assert.Contains(result.Errors, e => e.Column == "Bit");
        }

        [Fact]
        public void ImportCsv_WarnsAndDefaultsWhenValuesAreInvalid()
        {
            var result = ImportCsv("TagName,Address,Scale,Default,Length,Enum,Range\nT1,1,abc,xyz,zero,broken,10\n");

            var entry = Assert.Single(result.Entries);
            Assert.Equal(1.0, entry.Scale);
            Assert.Null(entry.Default);
            Assert.Equal(1, entry.Length);
            Assert.Empty(entry.Enum);
            Assert.Null(entry.RangeMin);
            Assert.Equal(5, result.Warnings.Count);
        }

        [Fact]
        public void ImportCsv_FailsWhenRequiredColumnsMissing()
        {
            var result = ImportCsv("Description,Unit\nsomething,Hz\n");

            Assert.Empty(result.Entries);
            Assert.Contains(result.Errors, e => e.Message.Contains("'TagName' and 'Address'"));
        }

        [Fact]
        public void ImportCsv_ReportsEmptyFile()
        {
            var result = ImportCsv("   \n\n");

            Assert.Empty(result.Entries);
            Assert.Contains(result.Errors, e => e.Message.Contains("empty"));
        }

        [Fact]
        public void GetCsvTemplate_RoundTripsThroughImport()
        {
            var service = new RegisterTemplateImportService();

            using var reader = new StringReader(service.GetCsvTemplate());
            var result = service.ImportCsv(reader);

            Assert.Empty(result.Errors);
            Assert.Equal(4, result.Entries.Count);
        }

        [Fact]
        public void ImportFromFile_ReadsSampleCsvFixture()
        {
            var result = new RegisterTemplateImportService()
                .ImportFromFile(TestDataPath("sample-register-map.csv"), AddressingConvention.Modicon);

            Assert.Empty(result.Errors);
            Assert.Equal(5, result.Entries.Count);
            Assert.Equal("sample-register-map", result.Template.Name);
            Assert.Equal(AddressingConvention.Modicon, result.Template.Addressing);

            var freq = result.Entries[0];
            Assert.Equal(0, freq.Address);
            Assert.Equal(0.01, freq.Scale);
            Assert.Equal(RegisterAccess.ReadOnly, freq.Access);

            var current = result.Entries[1];
            Assert.Equal(WordOrder.LittleEndian, current.WordOrder);
            Assert.Equal(2, current.Length);

            var command = result.Entries[2];
            Assert.Equal("Run", command.Enum[1]);
            Assert.Equal(RegisterAccess.ReadWrite, command.Access);

            Assert.Equal(5, result.Entries[3].Bit);
            Assert.Equal(PlcArea.DiscreteInput, result.Entries[4].RegisterType);
        }

        [Fact]
        public void ImportExcel_ReadsSampleXlsxFixture()
        {
            using var stream = File.OpenRead(TestDataPath("sample-register-map.xlsx"));

            var result = new RegisterTemplateImportService().ImportExcel(stream, AddressingConvention.Modicon);

            Assert.Empty(result.Errors);
            Assert.Equal(5, result.Entries.Count);
            Assert.Equal("VFD_OutputFreq", result.Entries[0].TagName);
            Assert.Equal(PlcArea.DiscreteInput, result.Entries[4].RegisterType);
        }

        [Fact]
        public void ImportExcel_ParsesFirstWorksheet()
        {
            using var stream = CreateWorkbook(new[]
            {
                new[] { "TagName", "Area", "Address", "DataType" },
                new[] { "Flow", "InputRegister", "30", "Float" },
            });

            var result = new RegisterTemplateImportService().ImportExcel(stream);

            Assert.Empty(result.Errors);
            var entry = Assert.Single(result.Entries);
            Assert.Equal("Flow", entry.TagName);
            Assert.Equal(PlcArea.InputRegister, entry.RegisterType);
            Assert.Equal(30, entry.Address);
            Assert.Equal(TagDataType.Float, entry.DataType);
        }

        [Fact]
        public void ImportFromFile_SelectsParserFromExtension()
        {
            var path = Path.Combine(Path.GetTempPath(), $"regmap-{Guid.NewGuid():N}.csv");
            File.WriteAllText(path, "TagName,Address\nT1,4\n");

            try
            {
                var result = new RegisterTemplateImportService().ImportFromFile(path);
                Assert.Equal("T1", Assert.Single(result.Entries).TagName);
                Assert.Equal(path, result.Template.SourceFile);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ToTag_MapsTemplateFieldsOntoTag()
        {
            var entry = ImportCsv(
                "TagName,Address,Unit,Scale,Offset,Access,Enum,Range\n" +
                "T1,9,bar,0.5,1,r,0=Closed;1=Open,0..10\n").Entries.Single();

            var tag = entry.ToTag();

            Assert.Equal("T1", tag.Name);
            Assert.Equal(9, tag.Address);
            Assert.Equal("bar", tag.Units);
            Assert.Equal(0.5, tag.Scale);
            Assert.Equal(1, tag.Offset);
            Assert.True(tag.IsReadOnly);
            Assert.True(tag.IsAlarmEnabled);
            Assert.Equal(0, tag.AlarmLow);
            Assert.Equal(10, tag.AlarmHigh);
            Assert.NotNull(tag.ValueEnum);
            Assert.Equal("Open", tag.ValueEnum![1]);
        }

        [Fact]
        public void Merge_AddsTagsCreatesGroupsAndSkipsDuplicateNames()
        {
            var service = new RegisterTemplateImportService();
            var tagService = new TagService();
            tagService.Tags.Add(new Tag { Name = "Existing", Address = 1 });

            var parsed = ImportCsv(
                "TagName,Group,Address\n" +
                "Existing,Pumps,2\n" +
                "New1,Pumps,3\n" +
                "New2,,4\n").Entries;

            var added = service.Merge(tagService, parsed, out var skipped);

            Assert.Equal(new[] { "New1", "New2" }, added.Select(t => t.Name));
            Assert.Equal(new[] { "Existing" }, skipped);

            var pumps = tagService.FindGroupByName("Pumps");
            Assert.NotNull(pumps);
            Assert.Equal("New1", Assert.Single(pumps!.Tags).Name);
            Assert.Equal(pumps.Id, added[0].GroupId);
            Assert.Equal("Default", added[1].Group);
        }

        private static string TestDataPath(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

        private static MemoryStream CreateWorkbook(string[][] rows)
        {
            var stream = new MemoryStream();
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

                var sheetData = new SheetData();
                uint rowIndex = 1;
                foreach (var row in rows)
                {
                    var sheetRow = new Row { RowIndex = rowIndex };
                    for (int i = 0; i < row.Length; i++)
                    {
                        sheetRow.Append(new Cell
                        {
                            CellReference = $"{(char)('A' + i)}{rowIndex}",
                            DataType = CellValues.InlineString,
                            InlineString = new InlineString(new Text(row[i])),
                        });
                    }

                    sheetData.Append(sheetRow);
                    rowIndex++;
                }

                worksheetPart.Worksheet = new Worksheet(sheetData);

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "RegisterMap",
                });
            }

            stream.Position = 0;
            return stream;
        }

        [Fact]
        public void ImportJson_ParsesArrayOfTagObjects()
        {
            var json = @"[
  { ""Name"": ""Pump_Status"", ""Address"": ""1"", ""Type"": ""Coil"" },
  { ""Name"": ""Flow_Rate"", ""Address"": ""40001"", ""Type"": ""HoldingRegister"", ""DataType"": ""Float32"", ""Endianness"": ""CDAB"", ""Length"": ""2"", ""Unit"": ""m3/h"" }
]";

            var result = new RegisterTemplateImportService().ImportJson(json, AddressingConvention.Modicon);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Entries.Count);

            var status = result.Entries[0];
            Assert.Equal("Pump_Status", status.TagName);
            Assert.Equal(PlcArea.Coil, status.RegisterType);
            Assert.Equal(0, status.Address);
            Assert.Equal(TagDataType.Bool, status.DataType);

            var flow = result.Entries[1];
            Assert.Equal("Flow_Rate", flow.TagName);
            Assert.Equal(PlcArea.HoldingRegister, flow.RegisterType);
            Assert.Equal(0, flow.Address);
            Assert.Equal(TagDataType.Float, flow.DataType);
            Assert.Equal(WordOrder.LittleEndian, flow.WordOrder);
            Assert.Equal(2, flow.Length);
            Assert.Equal("m3/h", flow.Unit);
        }

        [Fact]
        public void ImportYaml_ParsesArrayOfTagMappings()
        {
            var yaml = @"
- Name: Tank_Level
  Address: 100
  Type: HoldingRegister
  DataType: DINT
  Endianness: CDAB
  Length: 2
- Name: Run_Cmd
  Address: 5
  Type: Coil
";

            var result = new RegisterTemplateImportService().ImportYaml(yaml, AddressingConvention.OneBased);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Entries.Count);

            var level = result.Entries[0];
            Assert.Equal("Tank_Level", level.TagName);
            Assert.Equal(99, level.Address);
            Assert.Equal(TagDataType.Int32, level.DataType);
            Assert.Equal(WordOrder.LittleEndian, level.WordOrder);
            Assert.Equal(2, level.Length);

            var run = result.Entries[1];
            Assert.Equal("Run_Cmd", run.TagName);
            Assert.Equal(4, run.Address);
            Assert.Equal(TagDataType.Bool, run.DataType);
        }

        [Fact]
        public void ImportL5X_ParsesRockwellTagsAndAssignsSequentialAddresses()
        {
            var l5x = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<RSLogix5000Content SchemaRevision=""2.0"">
  <Controller>
    <Tags>
      <Tag Name=""Flow"" DataType=""REAL"" Dimensions=""0"" Description=""Flow rate"" />
      <Tag Name=""Level"" DataType=""DINT"" Dimensions=""0"" />
      <Tag Name=""Arr"" DataType=""REAL"" Dimensions=""3"" />
    </Tags>
  </Controller>
</RSLogix5000Content>";

            var result = new RegisterTemplateImportService().ImportL5X(l5x, AddressingConvention.ZeroBased);

            Assert.Empty(result.Errors);
            Assert.Equal(5, result.Entries.Count);

            var flow = result.Entries[0];
            Assert.Equal("Flow", flow.TagName);
            Assert.Equal(0, flow.Address);
            Assert.Equal(TagDataType.Float, flow.DataType);
            Assert.Equal(2, flow.Length);

            var level = result.Entries[1];
            Assert.Equal("Level", level.TagName);
            Assert.Equal(2, level.Address);
            Assert.Equal(TagDataType.Int32, level.DataType);
            Assert.Equal(2, level.Length);

            Assert.Equal("Arr[0]", result.Entries[2].TagName);
            Assert.Equal(4, result.Entries[2].Address);
            Assert.Equal("Arr[1]", result.Entries[3].TagName);
            Assert.Equal(6, result.Entries[3].Address);
            Assert.Equal("Arr[2]", result.Entries[4].TagName);
            Assert.Equal(8, result.Entries[4].Address);
        }

        [Fact]
        public void ImportFromFile_SelectsJsonYamlAndL5xByExtension()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"regmap-{Guid.NewGuid():N}");

            var jsonPath = basePath + ".json";
            var yamlPath = basePath + ".yaml";
            var l5xPath = basePath + ".l5x";

            File.WriteAllText(jsonPath, "[{ \"Name\": \"J1\", \"Address\": \"1\", \"Type\": \"HoldingRegister\" }]");
            File.WriteAllText(yamlPath, "- Name: Y1\n  Address: 2\n  Type: HoldingRegister\n");
            File.WriteAllText(l5xPath, "<?xml version=\"1.0\"?><RSLogix5000Content><Controller><Tags><Tag Name=\"L1\" DataType=\"REAL\" Dimensions=\"0\"/></Tags></Controller></RSLogix5000Content>");

            try
            {
                var jsonResult = new RegisterTemplateImportService().ImportFromFile(jsonPath);
                Assert.Equal("J1", Assert.Single(jsonResult.Entries).TagName);

                var yamlResult = new RegisterTemplateImportService().ImportFromFile(yamlPath);
                Assert.Equal("Y1", Assert.Single(yamlResult.Entries).TagName);

                var l5xResult = new RegisterTemplateImportService().ImportFromFile(l5xPath);
                Assert.Equal("L1", Assert.Single(l5xResult.Entries).TagName);
            }
            finally
            {
                File.Delete(jsonPath);
                File.Delete(yamlPath);
                File.Delete(l5xPath);
            }
        }
    }
}

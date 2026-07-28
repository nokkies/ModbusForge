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
    public class RegisterMapImportServiceTests
    {
        private static RegisterMapImportResult ImportCsv(string csv)
        {
            var service = new RegisterMapImportService();
            using var reader = new StringReader(csv);
            return service.ImportCsv(reader);
        }

        [Fact]
        public void ImportCsv_ParsesAllSupportedColumns()
        {
            var result = ImportCsv(
                "Name,Description,Group,Area,Address,DataType,Scale,Offset,Units,ReadOnly,AlarmHigh,AlarmLow\n" +
                "Pump1_Speed,Speed feedback,Pumps,HoldingRegister,100,UInt16,0.1,5,Hz,true,50,5\n");

            Assert.Empty(result.Errors);
            var tag = Assert.Single(result.Tags);
            Assert.Equal("Pump1_Speed", tag.Name);
            Assert.Equal("Speed feedback", tag.Description);
            Assert.Equal("Pumps", tag.Group);
            Assert.Equal(PlcArea.HoldingRegister, tag.Area);
            Assert.Equal(100, tag.Address);
            Assert.Equal(TagDataType.UInt16, tag.DataType);
            Assert.Equal(0.1, tag.Scale);
            Assert.Equal(5, tag.Offset);
            Assert.Equal("Hz", tag.Units);
            Assert.True(tag.IsReadOnly);
            Assert.True(tag.IsAlarmEnabled);
            Assert.Equal(50, tag.AlarmHigh);
            Assert.Equal(5, tag.AlarmLow);
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
            Assert.Equal(expected, Assert.Single(result.Tags).Area);
        }

        [Fact]
        public void ImportCsv_DefaultsBooleanDataTypeForBitAreas()
        {
            var result = ImportCsv("Name,Area,Address\nRunning,Coil,3\n");

            Assert.Equal(TagDataType.Bool, Assert.Single(result.Tags).DataType);
        }

        [Fact]
        public void ImportCsv_HandlesQuotedFieldsAndSemicolonDelimiter()
        {
            var result = ImportCsv("Name;Description;Address\nT1;\"Level, tank 1\";12\n");

            var tag = Assert.Single(result.Tags);
            Assert.Equal("Level, tank 1", tag.Description);
            Assert.Equal(12, tag.Address);
        }

        [Fact]
        public void ImportCsv_RejectsInvalidRowsButKeepsValidOnes()
        {
            var result = ImportCsv(
                "Name,Area,Address\n" +
                "Good,HoldingRegister,1\n" +
                ",HoldingRegister,2\n" +
                "BadAddress,HoldingRegister,abc\n" +
                "OutOfRange,HoldingRegister,70000\n" +
                "BadArea,Nonsense,4\n" +
                "Good,HoldingRegister,5\n");

            Assert.Equal(6, result.RowsRead);
            Assert.Equal("Good", Assert.Single(result.Tags).Name);
            Assert.Equal(5, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.Column == "Name" && e.Message.Contains("required"));
            Assert.Contains(result.Errors, e => e.Column == "Name" && e.Message.Contains("Duplicate"));
            Assert.Contains(result.Errors, e => e.Column == "Address" && e.Message.Contains("valid address"));
            Assert.Contains(result.Errors, e => e.Column == "Address" && e.Message.Contains("outside the valid range"));
            Assert.Contains(result.Errors, e => e.Column == "Area");
        }

        [Fact]
        public void ImportCsv_WarnsAndDefaultsWhenNumericFieldsAreInvalid()
        {
            var result = ImportCsv("Name,Address,Scale,AlarmHigh\nT1,1,abc,xyz\n");

            var tag = Assert.Single(result.Tags);
            Assert.Equal(1.0, tag.Scale);
            Assert.Null(tag.AlarmHigh);
            Assert.Equal(2, result.Warnings.Count);
        }

        [Fact]
        public void ImportCsv_FailsWhenRequiredColumnsMissing()
        {
            var result = ImportCsv("Description,Units\nsomething,Hz\n");

            Assert.Empty(result.Tags);
            Assert.Contains(result.Errors, e => e.Message.Contains("'Name' and 'Address'"));
        }

        [Fact]
        public void ImportCsv_ReportsEmptyFile()
        {
            var result = ImportCsv("   \n\n");

            Assert.Empty(result.Tags);
            Assert.Contains(result.Errors, e => e.Message.Contains("empty"));
        }

        [Fact]
        public void GetCsvTemplate_RoundTripsThroughImport()
        {
            var service = new RegisterMapImportService();

            using var reader = new StringReader(service.GetCsvTemplate());
            var result = service.ImportCsv(reader);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Tags.Count);
        }

        [Fact]
        public void ImportExcel_ParsesFirstWorksheet()
        {
            using var stream = CreateWorkbook(new[]
            {
                new[] { "Name", "Area", "Address", "DataType" },
                new[] { "Flow", "InputRegister", "30", "Float" },
            });

            var result = new RegisterMapImportService().ImportExcel(stream);

            Assert.Empty(result.Errors);
            var tag = Assert.Single(result.Tags);
            Assert.Equal("Flow", tag.Name);
            Assert.Equal(PlcArea.InputRegister, tag.Area);
            Assert.Equal(30, tag.Address);
            Assert.Equal(TagDataType.Float, tag.DataType);
        }

        [Fact]
        public void ImportFromFile_SelectsParserFromExtension()
        {
            var path = Path.Combine(Path.GetTempPath(), $"regmap-{Guid.NewGuid():N}.csv");
            File.WriteAllText(path, "Name,Address\nT1,4\n");

            try
            {
                var result = new RegisterMapImportService().ImportFromFile(path);
                Assert.Equal("T1", Assert.Single(result.Tags).Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Merge_AddsTagsCreatesGroupsAndSkipsDuplicateNames()
        {
            var service = new RegisterMapImportService();
            var tagService = new TagService();
            tagService.Tags.Add(new Tag { Name = "Existing", Address = 1 });

            var parsed = ImportCsv(
                "Name,Group,Address\n" +
                "Existing,Pumps,2\n" +
                "New1,Pumps,3\n" +
                "New2,,4\n").Tags;

            var added = service.Merge(tagService, parsed, out var skipped);

            Assert.Equal(new[] { "New1", "New2" }, added.Select(t => t.Name));
            Assert.Equal(new[] { "Existing" }, skipped);

            var pumps = tagService.FindGroupByName("Pumps");
            Assert.NotNull(pumps);
            Assert.Equal("New1", Assert.Single(pumps!.Tags).Name);
            Assert.Equal(pumps.Id, added[0].GroupId);
            Assert.Equal("Default", added[1].Group);
        }

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
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Parses device templates / register maps (CSV and Excel) into tag definitions.
    /// </summary>
    public class RegisterMapImportService : IRegisterMapImportService
    {
        private const int MaxModbusAddress = 65535;

        private readonly ILogger<RegisterMapImportService> _logger;

        private static readonly IReadOnlyDictionary<string, string> ColumnAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = "name",
                ["tag"] = "name",
                ["tagname"] = "name",
                ["symbol"] = "name",
                ["description"] = "description",
                ["comment"] = "description",
                ["group"] = "group",
                ["folder"] = "group",
                ["area"] = "area",
                ["type"] = "area",
                ["registertype"] = "area",
                ["address"] = "address",
                ["register"] = "address",
                ["offset"] = "offset",
                ["datatype"] = "datatype",
                ["format"] = "datatype",
                ["scale"] = "scale",
                ["gain"] = "scale",
                ["units"] = "units",
                ["unit"] = "units",
                ["eu"] = "units",
                ["readonly"] = "readonly",
                ["alarmhigh"] = "alarmhigh",
                ["highalarm"] = "alarmhigh",
                ["alarmlow"] = "alarmlow",
                ["lowalarm"] = "alarmlow",
            };

        private static readonly IReadOnlyDictionary<string, PlcArea> AreaAliases =
            new Dictionary<string, PlcArea>(StringComparer.OrdinalIgnoreCase)
            {
                ["holdingregister"] = PlcArea.HoldingRegister,
                ["holding"] = PlcArea.HoldingRegister,
                ["hr"] = PlcArea.HoldingRegister,
                ["4x"] = PlcArea.HoldingRegister,
                ["inputregister"] = PlcArea.InputRegister,
                ["input"] = PlcArea.InputRegister,
                ["ir"] = PlcArea.InputRegister,
                ["3x"] = PlcArea.InputRegister,
                ["coil"] = PlcArea.Coil,
                ["coils"] = PlcArea.Coil,
                ["0x"] = PlcArea.Coil,
                ["discreteinput"] = PlcArea.DiscreteInput,
                ["discrete"] = PlcArea.DiscreteInput,
                ["di"] = PlcArea.DiscreteInput,
                ["1x"] = PlcArea.DiscreteInput,
            };

        private static readonly IReadOnlyDictionary<string, TagDataType> DataTypeAliases =
            new Dictionary<string, TagDataType>(StringComparer.OrdinalIgnoreCase)
            {
                ["bool"] = TagDataType.Bool,
                ["boolean"] = TagDataType.Bool,
                ["bit"] = TagDataType.Bool,
                ["int16"] = TagDataType.Int16,
                ["int"] = TagDataType.Int16,
                ["short"] = TagDataType.Int16,
                ["uint16"] = TagDataType.UInt16,
                ["uint"] = TagDataType.UInt16,
                ["word"] = TagDataType.UInt16,
                ["int32"] = TagDataType.Int32,
                ["dint"] = TagDataType.Int32,
                ["long"] = TagDataType.Int32,
                ["uint32"] = TagDataType.UInt32,
                ["udint"] = TagDataType.UInt32,
                ["dword"] = TagDataType.UInt32,
                ["float"] = TagDataType.Float,
                ["real"] = TagDataType.Float,
                ["single"] = TagDataType.Float,
                ["double"] = TagDataType.Double,
                ["lreal"] = TagDataType.Double,
                ["string"] = TagDataType.String,
                ["text"] = TagDataType.String,
            };

        public RegisterMapImportService() : this(null)
        {
        }

        public RegisterMapImportService(ILogger<RegisterMapImportService>? logger)
        {
            _logger = logger ?? NullLogger<RegisterMapImportService>.Instance;
        }

        public RegisterMapImportResult ImportFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            var extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(filePath);
                return ImportExcel(stream);
            }

            using var reader = new StreamReader(filePath);
            return ImportCsv(reader);
        }

        public RegisterMapImportResult ImportCsv(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            var rows = new List<List<string>>();
            string? line;
            char delimiter = ',';
            bool delimiterDetected = false;

            while ((line = reader.ReadLine()) != null)
            {
                if (!delimiterDetected && !string.IsNullOrWhiteSpace(line))
                {
                    delimiter = DetectDelimiter(line);
                    delimiterDetected = true;
                }

                rows.Add(SplitDelimitedLine(line, delimiter));
            }

            return BuildResult(rows);
        }

        public RegisterMapImportResult ImportExcel(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            using var document = SpreadsheetDocument.Open(stream, false);
            var workbookPart = document.WorkbookPart
                ?? throw new InvalidDataException("The workbook does not contain any content.");

            var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault()
                ?? throw new InvalidDataException("The workbook does not contain any worksheets.");

            var sheetId = sheet.Id?.Value
                ?? throw new InvalidDataException("The first worksheet has no relationship id.");
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheetId);

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                ?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? new List<string>();

            var rows = new List<List<string>>();
            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                var values = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var columnIndex = GetColumnIndex(cell.CellReference?.Value);
                    while (values.Count < columnIndex)
                        values.Add(string.Empty);

                    values.Add(GetCellText(cell, sharedStrings));
                }

                rows.Add(values);
            }

            return BuildResult(rows);
        }

        public IReadOnlyList<Tag> Merge(TagService tagService, IEnumerable<Tag> tags, out IReadOnlyList<string> skippedNames)
        {
            ArgumentNullException.ThrowIfNull(tagService);
            ArgumentNullException.ThrowIfNull(tags);

            var added = new List<Tag>();
            var skipped = new List<string>();

            foreach (var tag in tags)
            {
                if (tagService.GetTagByName(tag.Name) != null)
                {
                    skipped.Add(tag.Name);
                    continue;
                }

                var groupName = string.IsNullOrWhiteSpace(tag.Group) ? "Default" : tag.Group;
                var group = tagService.FindGroupByName(groupName);
                if (group == null)
                {
                    group = new TagGroup { Name = groupName };
                    tagService.Groups.Add(group);
                }

                tag.Group = group.Name;
                tag.GroupId = group.Id;
                tagService.Tags.Add(tag);
                group.Tags.Add(tag);
                added.Add(tag);
            }

            _logger.LogInformation("Register map import merged {AddedCount} tags ({SkippedCount} skipped as duplicates)",
                added.Count, skipped.Count);

            skippedNames = skipped;
            return added;
        }

        public string GetCsvTemplate()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Name,Description,Group,Area,Address,DataType,Scale,Offset,Units,ReadOnly,AlarmHigh,AlarmLow");
            builder.AppendLine("Pump1_Speed,Pump 1 speed feedback,Pumps,HoldingRegister,100,UInt16,0.1,0,Hz,false,50,5");
            builder.AppendLine("Pump1_Running,Pump 1 run status,Pumps,Coil,10,Bool,1,0,,true,,");
            return builder.ToString();
        }

        private RegisterMapImportResult BuildResult(IReadOnlyList<List<string>> rows)
        {
            var result = new RegisterMapImportResult();

            var headerIndex = rows.ToList().FindIndex(r => r.Any(c => !string.IsNullOrWhiteSpace(c)));
            if (headerIndex < 0)
            {
                result.Errors.Add(new RegisterMapImportIssue { RowNumber = 1, Message = "The file is empty." });
                return result;
            }

            var columns = MapHeader(rows[headerIndex]);
            if (!columns.ContainsKey("name") || !columns.ContainsKey("address"))
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = headerIndex + 1,
                    Message = "The header row must contain at least 'Name' and 'Address' columns."
                });
                return result;
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.All(string.IsNullOrWhiteSpace))
                    continue;

                result.RowsRead++;
                ParseRow(row, columns, i + 1, seenNames, result);
            }

            return result;
        }

        private void ParseRow(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            int rowNumber,
            HashSet<string> seenNames,
            RegisterMapImportResult result)
        {
            var name = GetValue(row, columns, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add(new RegisterMapImportIssue { RowNumber = rowNumber, Column = "Name", Message = "Name is required." });
                return;
            }

            if (!seenNames.Add(name))
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "Name",
                    Message = $"Duplicate tag name '{name}' in the file."
                });
                return;
            }

            var addressText = GetValue(row, columns, "address");
            if (!int.TryParse(addressText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var address))
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "Address",
                    Message = $"'{addressText}' is not a valid address."
                });
                return;
            }

            if (address < 0 || address > MaxModbusAddress)
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "Address",
                    Message = $"Address {address} is outside the valid range 0-{MaxModbusAddress}."
                });
                return;
            }

            var area = PlcArea.HoldingRegister;
            var areaText = GetValue(row, columns, "area");
            if (!string.IsNullOrWhiteSpace(areaText))
            {
                if (AreaAliases.TryGetValue(Normalize(areaText), out var parsedArea))
                {
                    area = parsedArea;
                }
                else
                {
                    result.Errors.Add(new RegisterMapImportIssue
                    {
                        RowNumber = rowNumber,
                        Column = "Area",
                        Message = $"Unknown register area '{areaText}'."
                    });
                    return;
                }
            }

            var dataType = area is PlcArea.Coil or PlcArea.DiscreteInput ? TagDataType.Bool : TagDataType.UInt16;
            var dataTypeText = GetValue(row, columns, "datatype");
            if (!string.IsNullOrWhiteSpace(dataTypeText))
            {
                if (DataTypeAliases.TryGetValue(Normalize(dataTypeText), out var parsedType))
                {
                    dataType = parsedType;
                }
                else
                {
                    result.Errors.Add(new RegisterMapImportIssue
                    {
                        RowNumber = rowNumber,
                        Column = "DataType",
                        Message = $"Unknown data type '{dataTypeText}'."
                    });
                    return;
                }
            }

            var tag = new Tag
            {
                Name = name,
                Description = GetValue(row, columns, "description"),
                Group = string.IsNullOrWhiteSpace(GetValue(row, columns, "group")) ? "Default" : GetValue(row, columns, "group"),
                Area = area,
                Address = address,
                DataType = dataType,
                Units = GetValue(row, columns, "units"),
                Scale = ParseDouble(row, columns, "scale", 1.0, rowNumber, "Scale", result),
                Offset = ParseDouble(row, columns, "offset", 0.0, rowNumber, "Offset", result),
                IsReadOnly = ParseBool(GetValue(row, columns, "readonly")),
            };

            tag.AlarmHigh = ParseOptionalDouble(row, columns, "alarmhigh", rowNumber, "AlarmHigh", result);
            tag.AlarmLow = ParseOptionalDouble(row, columns, "alarmlow", rowNumber, "AlarmLow", result);
            tag.IsAlarmEnabled = tag.AlarmHigh.HasValue || tag.AlarmLow.HasValue;

            result.Tags.Add(tag);
        }

        private static double ParseDouble(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            string column,
            double fallback,
            int rowNumber,
            string displayName,
            RegisterMapImportResult result)
        {
            var text = GetValue(row, columns, column);
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            result.Warnings.Add(new RegisterMapImportIssue
            {
                RowNumber = rowNumber,
                Column = displayName,
                Message = $"'{text}' is not a number; using {fallback.ToString(CultureInfo.InvariantCulture)}."
            });
            return fallback;
        }

        private static double? ParseOptionalDouble(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            string column,
            int rowNumber,
            string displayName,
            RegisterMapImportResult result)
        {
            var text = GetValue(row, columns, column);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            result.Warnings.Add(new RegisterMapImportIssue
            {
                RowNumber = rowNumber,
                Column = displayName,
                Message = $"'{text}' is not a number; the alarm limit was ignored."
            });
            return null;
        }

        private static bool ParseBool(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var normalized = text.Trim();
            return normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("y", StringComparison.OrdinalIgnoreCase)
                || normalized == "1";
        }

        private static string GetValue(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string column)
        {
            if (!columns.TryGetValue(column, out var index) || index >= row.Count)
                return string.Empty;

            return row[index].Trim();
        }

        private static Dictionary<string, int> MapHeader(IReadOnlyList<string> header)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                var key = Normalize(header[i]);
                if (key.Length == 0)
                    continue;

                if (ColumnAliases.TryGetValue(key, out var canonical) && !map.ContainsKey(canonical))
                    map[canonical] = i;
            }

            return map;
        }

        private static string Normalize(string value) =>
            new string(value.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray());

        private static char DetectDelimiter(string line)
        {
            if (line.Contains('\t')) return '\t';
            if (line.Contains(';') && !line.Contains(',')) return ';';
            return ',';
        }

        internal static List<string> SplitDelimitedLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }

        private static int GetColumnIndex(string? cellReference)
        {
            if (string.IsNullOrEmpty(cellReference))
                return 0;

            int index = 0;
            foreach (var c in cellReference)
            {
                if (!char.IsLetter(c))
                    break;

                index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
            }

            return Math.Max(0, index - 1);
        }

        private static string GetCellText(Cell cell, IReadOnlyList<string> sharedStrings)
        {
            var value = cell.CellValue?.InnerText ?? string.Empty;

            if (cell.DataType?.Value == CellValues.SharedString
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex)
                && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
            {
                return sharedStrings[sharedIndex];
            }

            if (cell.DataType?.Value == CellValues.InlineString)
                return cell.InnerText;

            if (cell.DataType?.Value == CellValues.Boolean)
                return value == "1" ? "true" : "false";

            return value;
        }
    }
}

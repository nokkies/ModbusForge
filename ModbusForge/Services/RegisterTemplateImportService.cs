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
    /// Parses vendor register maps / device templates (CSV and Excel) into register templates.
    /// </summary>
    public class RegisterTemplateImportService : IRegisterTemplateImportService
    {
        private const int MaxModbusAddress = 65535;
        private const int MaxBitIndex = 15;

        private readonly ILogger<RegisterTemplateImportService> _logger;

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
                ["bit"] = "bit",
                ["bitnumber"] = "bit",
                ["datatype"] = "datatype",
                ["format"] = "datatype",
                ["wordorder"] = "wordorder",
                ["byteorder"] = "wordorder",
                ["endianness"] = "wordorder",
                ["length"] = "length",
                ["count"] = "length",
                ["registers"] = "length",
                ["scale"] = "scale",
                ["gain"] = "scale",
                ["multiplier"] = "scale",
                ["offset"] = "offset",
                ["unit"] = "unit",
                ["units"] = "unit",
                ["eu"] = "unit",
                ["access"] = "access",
                ["readwrite"] = "access",
                ["rw"] = "access",
                ["readonly"] = "readonly",
                ["enum"] = "enum",
                ["enumeration"] = "enum",
                ["states"] = "enum",
                ["default"] = "default",
                ["defaultvalue"] = "default",
                ["range"] = "range",
                ["min"] = "min",
                ["minimum"] = "min",
                ["max"] = "max",
                ["maximum"] = "max",
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
                ["float32"] = TagDataType.Float,
                ["double"] = TagDataType.Double,
                ["lreal"] = TagDataType.Double,
                ["float64"] = TagDataType.Double,
                ["string"] = TagDataType.String,
                ["text"] = TagDataType.String,
            };

        private static readonly IReadOnlyDictionary<string, WordOrder> WordOrderAliases =
            new Dictionary<string, WordOrder>(StringComparer.OrdinalIgnoreCase)
            {
                ["big"] = WordOrder.BigEndian,
                ["bigendian"] = WordOrder.BigEndian,
                ["be"] = WordOrder.BigEndian,
                ["abcd"] = WordOrder.BigEndian,
                ["msw"] = WordOrder.BigEndian,
                ["little"] = WordOrder.LittleEndian,
                ["littleendian"] = WordOrder.LittleEndian,
                ["le"] = WordOrder.LittleEndian,
                ["cdab"] = WordOrder.LittleEndian,
                ["lsw"] = WordOrder.LittleEndian,
                ["wordswap"] = WordOrder.LittleEndian,
                ["swapped"] = WordOrder.LittleEndian,
            };

        private static readonly IReadOnlyDictionary<string, RegisterAccess> AccessAliases =
            new Dictionary<string, RegisterAccess>(StringComparer.OrdinalIgnoreCase)
            {
                ["r"] = RegisterAccess.ReadOnly,
                ["ro"] = RegisterAccess.ReadOnly,
                ["read"] = RegisterAccess.ReadOnly,
                ["readonly"] = RegisterAccess.ReadOnly,
                ["rw"] = RegisterAccess.ReadWrite,
                ["wr"] = RegisterAccess.ReadWrite,
                ["readwrite"] = RegisterAccess.ReadWrite,
                ["w"] = RegisterAccess.ReadWrite,
                ["write"] = RegisterAccess.ReadWrite,
            };

        /// <summary>Registers occupied by each data type when no explicit length is given.</summary>
        private static readonly IReadOnlyDictionary<TagDataType, int> DefaultLengths =
            new Dictionary<TagDataType, int>
            {
                [TagDataType.Bool] = 1,
                [TagDataType.Int16] = 1,
                [TagDataType.UInt16] = 1,
                [TagDataType.Int32] = 2,
                [TagDataType.UInt32] = 2,
                [TagDataType.Float] = 2,
                [TagDataType.Double] = 4,
                [TagDataType.String] = 1,
            };

        public RegisterTemplateImportService() : this(null)
        {
        }

        public RegisterTemplateImportService(ILogger<RegisterTemplateImportService>? logger)
        {
            _logger = logger ?? NullLogger<RegisterTemplateImportService>.Instance;
        }

        public RegisterTemplateImportResult ImportFromFile(string filePath, AddressingConvention addressing = AddressingConvention.ZeroBased)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            RegisterTemplateImportResult result;
            if (string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(filePath);
                result = ImportExcel(stream, addressing);
            }
            else
            {
                using var reader = new StreamReader(filePath);
                result = ImportCsv(reader, addressing);
            }

            result.Template.SourceFile = filePath;
            result.Template.Name = Path.GetFileNameWithoutExtension(filePath);
            return result;
        }

        public RegisterTemplateImportResult ImportCsv(TextReader reader, AddressingConvention addressing = AddressingConvention.ZeroBased)
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

            return BuildResult(rows, addressing);
        }

        public RegisterTemplateImportResult ImportExcel(Stream stream, AddressingConvention addressing = AddressingConvention.ZeroBased)
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

            return BuildResult(rows, addressing);
        }

        public IReadOnlyList<Tag> Merge(TagService tagService, IEnumerable<RegisterTemplateEntry> entries, out IReadOnlyList<string> skippedNames)
        {
            ArgumentNullException.ThrowIfNull(tagService);
            ArgumentNullException.ThrowIfNull(entries);

            var added = new List<Tag>();
            var skipped = new List<string>();

            foreach (var entry in entries)
            {
                if (tagService.GetTagByName(entry.TagName) != null)
                {
                    skipped.Add(entry.TagName);
                    continue;
                }

                var groupName = string.IsNullOrWhiteSpace(entry.Group) ? "Default" : entry.Group;
                var group = tagService.FindGroupByName(groupName);
                if (group == null)
                {
                    group = new TagGroup { Name = groupName };
                    tagService.Groups.Add(group);
                }

                var tag = entry.ToTag();
                tag.Group = group.Name;
                tag.GroupId = group.Id;
                tagService.Tags.Add(tag);
                group.Tags.Add(tag);
                added.Add(tag);
            }

            _logger.LogInformation("Register template import merged {AddedCount} tags ({SkippedCount} skipped as duplicates)",
                added.Count, skipped.Count);

            skippedNames = skipped;
            return added;
        }

        public string GetCsvTemplate()
        {
            var builder = new StringBuilder();
            builder.AppendLine("TagName,Description,Group,RegisterType,Address,Bit,DataType,WordOrder,Length,Scale,Offset,Unit,Access,Enum,Default,Range");
            builder.AppendLine("Pump1_Speed,Pump 1 speed feedback,Pumps,HoldingRegister,100,,UInt16,BigEndian,1,0.1,0,Hz,rw,,0,0..50");
            builder.AppendLine("Pump1_Flow,Pump 1 flow,Pumps,InputRegister,200,,Float,LittleEndian,2,1,0,m3/h,r,,,0..250");
            builder.AppendLine("Pump1_Mode,Pump 1 mode,Pumps,HoldingRegister,110,,UInt16,BigEndian,1,1,0,,rw,0=Off;1=Hand;2=Auto,2,");
            builder.AppendLine("Pump1_Fault,Pump 1 fault bit 3 of status word,Pumps,HoldingRegister,120,3,Bool,BigEndian,1,1,0,,r,0=Ok;1=Fault,,");
            return builder.ToString();
        }

        private RegisterTemplateImportResult BuildResult(IReadOnlyList<List<string>> rows, AddressingConvention addressing)
        {
            var result = new RegisterTemplateImportResult();
            result.Template.Addressing = addressing;

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
                    Message = "The header row must contain at least 'TagName' and 'Address' columns."
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
                ParseRow(row, columns, i + 1, seenNames, addressing, result);
            }

            return result;
        }

        private void ParseRow(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            int rowNumber,
            HashSet<string> seenNames,
            AddressingConvention addressing,
            RegisterTemplateImportResult result)
        {
            var name = GetValue(row, columns, "name");
            if (!TryParseName(name, rowNumber, seenNames, result))
                return;

            if (!TryParseStrictFields(row, columns, rowNumber, addressing, result, out var strict))
                return;

            var (rangeMin, rangeMax) = ParseRange(row, columns, rowNumber, result);

            var group = GetValue(row, columns, "group");
            var entry = new RegisterTemplateEntry
            {
                TagName = name,
                SourceRow = rowNumber,
                Description = GetValue(row, columns, "description"),
                Group = string.IsNullOrWhiteSpace(group) ? "Default" : group,
                RegisterType = strict.Area,
                Address = strict.Address,
                RawAddress = strict.RawAddress,
                Bit = strict.Bit,
                DataType = strict.DataType,
                WordOrder = strict.WordOrder,
                Length = ParseLength(row, columns, strict.DataType, rowNumber, result),
                Scale = ParseDouble(row, columns, "scale", 1.0, rowNumber, "Scale", result),
                Offset = ParseDouble(row, columns, "offset", 0.0, rowNumber, "Offset", result),
                Unit = GetValue(row, columns, "unit"),
                Access = strict.Access,
                Enum = ParseEnum(GetValue(row, columns, "enum"), rowNumber, result),
                Default = ParseOptionalDouble(row, columns, "default", rowNumber, "Default", result),
                RangeMin = rangeMin,
                RangeMax = rangeMax,
            };

            result.Template.Entries.Add(entry);
        }

        /// <summary>Fields whose values reject the whole row when they cannot be parsed.</summary>
        private readonly record struct StrictFields(
            PlcArea Area,
            int Address,
            string RawAddress,
            int? Bit,
            TagDataType DataType,
            WordOrder WordOrder,
            RegisterAccess Access);

        private static bool TryParseStrictFields(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            int rowNumber,
            AddressingConvention addressing,
            RegisterTemplateImportResult result,
            out StrictFields fields)
        {
            fields = default;

            if (!TryParseLookup(GetValue(row, columns, "area"), AreaAliases, PlcArea.HoldingRegister,
                    rowNumber, "RegisterType", "register area", result, out var area))
                return false;

            var rawAddress = GetValue(row, columns, "address");
            if (!TryResolveAddress(rawAddress, addressing, rowNumber, result, ref area, out var address))
                return false;

            if (!TryParseLookup(GetValue(row, columns, "datatype"), DataTypeAliases, DefaultDataTypeFor(area),
                    rowNumber, "DataType", "data type", result, out var dataType))
                return false;

            if (!TryParseLookup(GetValue(row, columns, "wordorder"), WordOrderAliases, WordOrder.BigEndian,
                    rowNumber, "WordOrder", "word order", result, out var wordOrder))
                return false;

            if (!TryParseLookup(GetValue(row, columns, "access"), AccessAliases, RegisterAccess.ReadWrite,
                    rowNumber, "Access", "access mode", result, out var access))
                return false;

            if (ParseBool(GetValue(row, columns, "readonly")))
                access = RegisterAccess.ReadOnly;

            if (!TryParseBit(GetValue(row, columns, "bit"), rowNumber, result, out var bit))
                return false;

            fields = new StrictFields(area, address, rawAddress, bit, dataType, wordOrder, access);
            return true;
        }

        private static TagDataType DefaultDataTypeFor(PlcArea area) =>
            area is PlcArea.Coil or PlcArea.DiscreteInput ? TagDataType.Bool : TagDataType.UInt16;

        private static bool TryParseName(string name, int rowNumber, HashSet<string> seenNames, RegisterTemplateImportResult result)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add(new RegisterMapImportIssue { RowNumber = rowNumber, Column = "TagName", Message = "TagName is required." });
                return false;
            }

            if (!seenNames.Add(name))
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "TagName",
                    Message = $"Duplicate tag name '{name}' in the file."
                });
                return false;
            }

            return true;
        }

        /// <summary>
        /// Converts a source address to a protocol (0-based) address. Modicon addresses also
        /// determine the register area, which overrides any area column.
        /// </summary>
        internal static bool TryResolveAddress(
            string rawAddress,
            AddressingConvention addressing,
            int rowNumber,
            RegisterTemplateImportResult result,
            ref PlcArea area,
            out int address)
        {
            address = 0;
            var text = rawAddress.Trim();

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "Address",
                    Message = $"'{rawAddress}' is not a valid address."
                });
                return false;
            }

            switch (addressing)
            {
                case AddressingConvention.OneBased:
                    address = parsed - 1;
                    break;

                case AddressingConvention.Modicon:
                    if (!TryConvertModicon(parsed, out area, out address))
                    {
                        result.Errors.Add(new RegisterMapImportIssue
                        {
                            RowNumber = rowNumber,
                            Column = "Address",
                            Message = $"'{rawAddress}' is not a Modicon address (expected e.g. 40001, 30001, 10001 or 000001)."
                        });
                        return false;
                    }
                    break;

                default:
                    address = parsed;
                    break;
            }

            if (address < 0 || address > MaxModbusAddress)
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "Address",
                    Message = $"Address {address} is outside the valid range 0-{MaxModbusAddress}."
                });
                return false;
            }

            return true;
        }

        /// <summary>
        /// Splits a 5- or 6-digit Modicon address (e.g. 40001 / 400001) into area + 0-based offset.
        /// </summary>
        internal static bool TryConvertModicon(int modicon, out PlcArea area, out int address)
        {
            area = PlcArea.HoldingRegister;
            address = 0;

            if (modicon <= 0)
                return false;

            var digits = modicon.ToString(CultureInfo.InvariantCulture).Length;
            int prefix;
            int offset;

            if (digits <= 5)
            {
                prefix = modicon / 10000;
                offset = modicon % 10000;
            }
            else if (digits == 6)
            {
                prefix = modicon / 100000;
                offset = modicon % 100000;
            }
            else
            {
                return false;
            }

            area = prefix switch
            {
                0 => PlcArea.Coil,
                1 => PlcArea.DiscreteInput,
                3 => PlcArea.InputRegister,
                4 => PlcArea.HoldingRegister,
                _ => PlcArea.HoldingRegister,
            };

            if (prefix is not (0 or 1 or 3 or 4) || offset == 0)
                return false;

            address = offset - 1;
            return true;
        }

        private static bool TryParseBit(string text, int rowNumber, RegisterTemplateImportResult result, out int? bit)
        {
            bit = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 0 || parsed > MaxBitIndex)
            {
                result.Errors.Add(new RegisterMapImportIssue
                {
                    RowNumber = rowNumber,
                    Column = "Bit",
                    Message = $"'{text}' is not a bit index in the range 0-{MaxBitIndex}."
                });
                return false;
            }

            bit = parsed;
            return true;
        }

        private static int ParseLength(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            TagDataType dataType,
            int rowNumber,
            RegisterTemplateImportResult result)
        {
            var fallback = DefaultLengths[dataType];
            var text = GetValue(row, columns, "length");
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                return parsed;

            result.Warnings.Add(new RegisterMapImportIssue
            {
                RowNumber = rowNumber,
                Column = "Length",
                Message = $"'{text}' is not a positive register count; using {fallback}."
            });
            return fallback;
        }

        /// <summary>Parses "0=Off;1=On" (also accepting ',' or '|' separators) into a value map.</summary>
        internal static Dictionary<int, string> ParseEnum(string text, int rowNumber, RegisterTemplateImportResult result)
        {
            var map = new Dictionary<int, string>();
            if (string.IsNullOrWhiteSpace(text))
                return map;

            foreach (var part in text.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length != 2
                    || !int.TryParse(pieces[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var key))
                {
                    result.Warnings.Add(new RegisterMapImportIssue
                    {
                        RowNumber = rowNumber,
                        Column = "Enum",
                        Message = $"'{part.Trim()}' is not a 'value=label' pair; it was ignored."
                    });
                    continue;
                }

                map[key] = pieces[1].Trim();
            }

            return map;
        }

        /// <summary>
        /// Reads range limits from either a combined "Range" column ("0..100", "0-100", "0:100")
        /// or separate Min/Max columns.
        /// </summary>
        private static (double? min, double? max) ParseRange(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            int rowNumber,
            RegisterTemplateImportResult result)
        {
            var min = ParseOptionalDouble(row, columns, "min", rowNumber, "Min", result);
            var max = ParseOptionalDouble(row, columns, "max", rowNumber, "Max", result);

            var text = GetValue(row, columns, "range");
            if (string.IsNullOrWhiteSpace(text))
                return (min, max);

            var parts = text.Split(new[] { "..", "...", ":", " to ", "-" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var low)
                && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
            {
                return (low, high);
            }

            result.Warnings.Add(new RegisterMapImportIssue
            {
                RowNumber = rowNumber,
                Column = "Range",
                Message = $"'{text}' is not a range like '0..100'; it was ignored."
            });
            return (min, max);
        }

        private static bool TryParseLookup<T>(
            string text,
            IReadOnlyDictionary<string, T> aliases,
            T fallback,
            int rowNumber,
            string column,
            string description,
            RegisterTemplateImportResult result,
            out T value)
        {
            value = fallback;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (aliases.TryGetValue(Normalize(text), out var parsed))
            {
                value = parsed;
                return true;
            }

            result.Errors.Add(new RegisterMapImportIssue
            {
                RowNumber = rowNumber,
                Column = column,
                Message = $"Unknown {description} '{text}'."
            });
            return false;
        }

        private static double ParseDouble(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> columns,
            string column,
            double fallback,
            int rowNumber,
            string displayName,
            RegisterTemplateImportResult result)
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
            RegisterTemplateImportResult result)
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
                Message = $"'{text}' is not a number; the value was ignored."
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

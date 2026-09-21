using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Importer for Schneider Unity Pro / Unity Pro XL (Quantum) FEF project files
    /// (<c>.XEF</c> / <c>.FEF</c>, XML; possibly gzip-compressed). Parses the symbolic
    /// tag table and the hand-written FBD logic sections into sets of
    /// <see cref="VisualNode"/>/<see cref="NodeConnection"/> that load directly into the
    /// ModbusForge visual simulation editor and run live against the Modbus server.
    ///
    /// Design goals:
    /// <list type="bullet">
    /// <item>Lossless on the parts ModbusForge models natively (AND/OR/NOT, RS,
    /// TON/TOF/TP, CTU/CTD/CTUD, comparators, math, MOVE, trig, word-level logic).</item>
    /// <item>Everything else (custom FBDs such as PID, scaling, sequence, DNP3, modbus,
    /// etc.) becomes a clearly-labelled "Opaque FB" node that preserves the instance
    /// name, wire topology, and any pins that map to addressed tags — so the simulation
    /// still runs end-to-end and the user can see what is held as an opaque block.</item>
    /// <item>Every mapped / unmappable element is reported in
    /// <see cref="PlcXmlImportResult"/> so the user knows exactly what was converted
    /// and what was preserved as a stub.</item>
    /// </list>
    /// </summary>
    public sealed class PlcXmlImporter
    {
        /// <summary>Placeholder element type used for custom FBDs that ModbusForge
        /// cannot model natively. The node's Name carries the Unity Pro type so it is
        /// visible on the canvas.</summary>
        public const PlcElementType Opaque = PlcElementType.SignalGenerator;

        private static readonly Regex TopologicalAddressRegex = new(
            @"^%?(MW|IW|IB|QB|IBI|MB|QB|Q|M|I)\s*\.?\s*(\d+)\s*(?:\.(\d+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ConstantRegex = new(
            @"^#?[\s\-+]?[0-9][0-9a-fA-F_.]*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Map of Unity Pro FBD function-block type names to the ModbusForge
        /// <see cref="PlcElementType"/> they correspond to. A value of <c>null</c> means
        /// "map to an Opaque FB"; a key that is absent means "Opaque FB". The map is
        /// case-insensitive.
        /// </summary>
        public static IReadOnlyDictionary<string, PlcElementType?> TypeMapping { get; } = BuildTypeMapping();

        private static Dictionary<string, PlcElementType?> BuildTypeMapping()
        {
            return new Dictionary<string, PlcElementType?>(StringComparer.OrdinalIgnoreCase)
            {
                // Boolean logic
                ["AND"] = PlcElementType.AND,
                ["AND_BOOL"] = PlcElementType.AND,
                ["AND_WORD"] = PlcElementType.AND,
                ["AND_DWORD"] = PlcElementType.AND,
                ["OR"] = PlcElementType.OR,
                ["OR_BOOL"] = PlcElementType.OR,
                ["OR_WORD"] = PlcElementType.OR,
                ["OR_DWORD"] = PlcElementType.OR,
                ["XOR"] = PlcElementType.OR,
                ["NOT"] = PlcElementType.NOT,

                // Latches
                ["RS"] = PlcElementType.RS,
                ["SR"] = PlcElementType.RS,
                ["SET"] = PlcElementType.RS,
                ["RESET"] = PlcElementType.RS,

                // Timers
                ["TON"] = PlcElementType.TON,
                ["TOF"] = PlcElementType.TOF,
                ["TP"] = PlcElementType.TP,

                // Counters
                ["CTU"] = PlcElementType.CTU,
                ["CTU_INT"] = PlcElementType.CTU,
                ["CTU_UDINT"] = PlcElementType.CTU,
                ["CTD"] = PlcElementType.CTD,
                ["CTUD"] = PlcElementType.CTC,
                ["CTC"] = PlcElementType.CTC,

                // Triggers — R_TRIG maps to the EdgeDetect block (rising edge).
                ["R_TRIG"] = PlcElementType.EdgeDetect,
                ["F_TRIG"] = null,

                // Comparators (int + real variants)
                ["EQ"] = PlcElementType.COMPARE_EQ,
                ["EQ_BOOL"] = PlcElementType.COMPARE_EQ,
                ["EQ_WORD"] = PlcElementType.COMPARE_EQ,
                ["EQ_DWORD"] = PlcElementType.COMPARE_EQ,
                ["EQ_INT"] = PlcElementType.COMPARE_EQ,
                ["EQ_UINT"] = PlcElementType.COMPARE_EQ,
                ["EQ_REAL"] = PlcElementType.COMPARE_EQ_REAL,
                ["NE"] = PlcElementType.COMPARE_NE,
                ["NE_BOOL"] = PlcElementType.COMPARE_NE,
                ["NE_WORD"] = PlcElementType.COMPARE_NE,
                ["NE_DWORD"] = PlcElementType.COMPARE_NE,
                ["NE_INT"] = PlcElementType.COMPARE_NE,
                ["NE_UINT"] = PlcElementType.COMPARE_NE,
                ["NE_REAL"] = PlcElementType.COMPARE_NE_REAL,
                ["GT"] = PlcElementType.COMPARE_GT,
                ["GT_INT"] = PlcElementType.COMPARE_GT,
                ["GT_UINT"] = PlcElementType.COMPARE_GT,
                ["GT_REAL"] = PlcElementType.COMPARE_GT_REAL,
                ["LT"] = PlcElementType.COMPARE_LT,
                ["LT_INT"] = PlcElementType.COMPARE_LT,
                ["LT_UINT"] = PlcElementType.COMPARE_LT,
                ["LT_REAL"] = PlcElementType.COMPARE_LT_REAL,
                ["GE"] = PlcElementType.COMPARE_GE,
                ["GE_INT"] = PlcElementType.COMPARE_GE,
                ["GE_UINT"] = PlcElementType.COMPARE_GE,
                ["GE_REAL"] = PlcElementType.COMPARE_GE_REAL,
                ["LE"] = PlcElementType.COMPARE_LE,
                ["LE_INT"] = PlcElementType.COMPARE_LE,
                ["LE_UINT"] = PlcElementType.COMPARE_LE,
                ["LE_REAL"] = PlcElementType.COMPARE_LE_REAL,

                // Math (int + real variants)
                ["ADD"] = PlcElementType.MATH_ADD,
                ["ADD_REAL"] = PlcElementType.MATH_ADD_REAL,
                ["ADD_UINT"] = PlcElementType.MATH_ADD,
                ["SUB"] = PlcElementType.MATH_SUB,
                ["SUB_REAL"] = PlcElementType.MATH_SUB_REAL,
                ["SUB_INT"] = PlcElementType.MATH_SUB,
                ["MUL"] = PlcElementType.MATH_MUL,
                ["MUL_REAL"] = PlcElementType.MATH_MUL_REAL,
                ["DIV"] = PlcElementType.MATH_DIV,
                ["DIV_REAL"] = PlcElementType.MATH_DIV_REAL,

                // Everything below is explicitly Opaque (preserved as a stub with its
                // wires and tag bindings, but no native ModbusForge equivalent).
                ["MOVE"] = null,
                ["MOVE_INT_ARINT"] = null,
                ["MOVE_WORD_ARWORD"] = null,
                ["MOVE_REAL_ARREAL"] = null,
                ["MOVE_DINT_ARDINT"] = null,
                ["ABS"] = null,
                ["EXP"] = null,
                ["EXPT_REAL"] = null,
                ["SQRT"] = null,
                ["SQRT_REAL"] = null,
                ["SIN"] = null,
                ["COS"] = null,
                ["TAN"] = null,
                ["DEG_TO_RAD"] = null,
                ["RAD_TO_DEG"] = null,
                ["LOG"] = null,
                ["LN"] = null,
                ["LIMIT"] = null,
                ["LIMIT_REAL"] = null,
                ["LIMIT_IND"] = null,
                ["LIMIT_IND_DINT"] = null,
                ["AVGMV"] = null,
                ["AVE"] = null,
                ["AVE_REAL"] = null,
                ["SCALING"] = null,
                ["I_SCALE_WARN"] = null,
                ["LOOKUP_TABLE1"] = null,
                ["RAMP"] = null,
                ["FREQ_CALC"] = null,
                ["SAMPLETM"] = null,
                ["MULTIME"] = null,
                ["MAX_TIME"] = null,
                ["MUX"] = null,
                ["SEL"] = null,
                ["SHL"] = null,
                ["SHR"] = null,
                ["ROL_ARINT"] = null,
                ["ROL_ARREAL"] = null,
                ["ROR_ARDINT"] = null,
                ["BYTE_AS_WORD"] = null,
                ["WORD_AS_DWORD"] = null,
                ["_WORD_AS_DWORD"] = null,
                ["WORD_AS_REAL"] = null,
                ["REAL_AS_WORD"] = null,
                ["INT_AS_BYTE"] = null,
                ["BYTE_AS_INT"] = null,
                ["DINT_AS_WORD"] = null,
                ["DWORD_AS_WORD"] = null,
                ["DINT_TO_DWORD"] = null,
                ["DWORD_TO_DINT"] = null,
                ["INT_TO_BIT"] = null,
                ["BIT_TO_INT"] = null,
                ["INT_TO_BYTE"] = null,
                ["BYTE_TO_INT"] = null,
                ["INT_TO_WORD"] = null,
                ["WORD_TO_INT"] = null,
                ["INT_TO_DINT"] = null,
                ["DINT_TO_INT"] = null,
                ["INT_TO_REAL"] = null,
                ["REAL_TO_INT"] = null,
                ["INT_TO_DWORD"] = null,
                ["DWORD_TO_INT"] = null,
                ["INT_TO_UINT"] = null,
                ["UINT_TO_INT"] = null,
                ["INT_TO_TIME"] = null,
                ["TIME_TO_INT"] = null,
                ["TIME_TO_REAL"] = null,
                ["TIME_TO_DINT"] = null,
                ["REAL_TO_TIME"] = null,
                ["REAL_TO_DINT"] = null,
                ["DINT_TO_REAL"] = null,
                ["UINT_TO_REAL"] = null,
                ["DWORD_TO_REAL"] = null,
                ["UDINT_TO_REAL"] = null,
                ["BOOL_TO_INT"] = null,
                ["BYTE_TO_BIT"] = null,
                ["BIT_TO_BYTE"] = null,
                ["BIT_TO_WORD"] = null,
                ["WORD_TO_BIT"] = null,
                ["BCD_TO_INT"] = null,
                ["INT_TO_STRING"] = null,
                ["CONCAT_STR"] = null,
                ["EXTRACT"] = null,
            };
        }

        /// <summary>Reads the XEF file (plain XML or gzip-compressed) and parses it.</summary>
        public PlcXmlImportResult Import(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", nameof(path));

            var xef = TryReadXml(path);
            if (xef == null)
            {
                return PlcXmlImportResult.Failure($"Could not read {Path.GetFileName(path)} as XML (or gzip-compressed XML).");
            }

            return ImportFromXml(xef, Path.GetFileName(path));
        }

        /// <summary>
        /// Parses an already-loaded XEF document. Exposed so tests can feed small
        /// inline samples without writing files.
        /// </summary>
        public PlcXmlImportResult ImportFromXml(XDocument xef, string sourceName)
        {
            var result = new PlcXmlImportResult { SourceFile = sourceName };
            if (xef.Root?.Name.LocalName != "FEFExchangeFile")
            {
                result.AddError($"Root element is '{xef.Root?.Name.LocalName ?? "(none)"}' — expected FEFExchangeFile.");
                return result;
            }

            var tags = ParseTagTable(xef, result);
            var sections = ParseSections(xef, tags, result);
            ParseHardware(xef, result);

            // Unity Pro reuses instance names (".1", "FBI_12", ...) across networks
            // AND across programs. The editor and the simulation engine both key by
            // global node id, so enforce uniqueness across the entire import here —
            // duplicate ids crash the engine when the graph loads.
            EnsureGlobalNodeIdUniqueness(result);

            result.Sections = sections;
            result.TagCount = tags.Count;
            result.SectionsFound = sections.Count;
            result.Success = result.Errors.Count == 0;

            return result;
        }

        private static void EnsureGlobalNodeIdUniqueness(PlcXmlImportResult result)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in result.Sections)
            {
                foreach (var node in section.Nodes)
                {
                    if (seen.Add(node.Id)) continue;

                    var suffix = 2;
                    string candidate;
                    do { candidate = $"{node.Id}_{suffix++}"; } while (!seen.Add(candidate));

                    // Rewire every connection that pointed at the colliding id.
                    var oldId = node.Id;
                    node.Id = candidate;
                    foreach (var conn in section.Connections)
                    {
                        if (conn.SourceNodeId == oldId) conn.SourceNodeId = candidate;
                        if (conn.TargetNodeId == oldId) conn.TargetNodeId = candidate;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Tag table
        // ------------------------------------------------------------------

        private static Dictionary<string, PlcAddressReference> ParseTagTable(XDocument xef, PlcXmlImportResult result)
        {
            var tags = new Dictionary<string, PlcAddressReference>(StringComparer.Ordinal);
            var dataBlock = xef.Descendants("dataBlock").FirstOrDefault();
            if (dataBlock == null)
            {
                result.AddWarning("No <dataBlock> found — tags will be unresolved.");
                return tags;
            }

            foreach (var v in dataBlock.Elements("variables"))
            {
                var name = (string?)v.Attribute("name");
                var address = (string?)v.Attribute("topologicalAddress");
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!string.IsNullOrWhiteSpace(address))
                {
                    var reference = TryParseTopologicalAddress(address);
                    if (reference == null)
                    {
                        result.AddWarning($"Unparseable topological address '{address}' on tag '{name}'.");
                    }
                    else if (!tags.TryAdd(name, reference))
                    {
                        result.AddWarning($"Duplicate tag '{name}' — keeping first.");
                    }
                }
                else
                {
                    // No topological address (internal tag). Keep a placeholder so
                    // wire resolution still works; the block renders unbound.
                    if (!tags.TryAdd(name, new PlcAddressReference { SymbolicName = name }))
                    {
                        result.AddWarning($"Duplicate tag '{name}' — keeping first.");
                    }
                }
            }

            return tags;
        }

        /// <summary>
        /// Parses a Unity Pro topological address like <c>%MW2002</c>, <c>%IW1000</c>,
        /// <c>%IB0.2</c>, <c>%QB0.3</c> into a <see cref="PlcAddressReference"/>.
        /// Coil/discrete bit offsets expand to <c>word*16+bit</c>.
        /// </summary>
        public static PlcAddressReference? TryParseTopologicalAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            var match = TopologicalAddressRegex.Match(address.Trim());
            if (!match.Success) return null;

            var areaText = match.Groups[1].Value.ToUpperInvariant();
            var word = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var bit = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : (int?)null;

            var area = areaText switch
            {
                "MW" or "M" => PlcArea.HoldingRegister,
                "IW" or "I" => PlcArea.InputRegister,
                "IB" or "IBI" => PlcArea.DiscreteInput,
                "QB" or "MB" or "Q" => PlcArea.Coil,
                _ => PlcArea.HoldingRegister
            };

            var finalAddress = bit.HasValue ? word * 16 + bit.Value : word;

            return new PlcAddressReference
            {
                Area = area,
                Address = finalAddress,
                SymbolicName = address
            };
        }

        // ------------------------------------------------------------------
        // Sections (FBD programs)
        // ------------------------------------------------------------------

        private static List<PlcXmlSection> ParseSections(
            XDocument xef,
            Dictionary<string, PlcAddressReference> tags,
            PlcXmlImportResult result)
        {
            var sections = new List<PlcXmlSection>();
            var sectionOrdinal = 0;
            var assignments = ParseTaskAssignments(xef, result);

            foreach (var program in xef.Descendants("program"))
            {
                var ident = program.Descendants("identProgram").FirstOrDefault();
                var sectionName = ident?.Attribute("name")?.Value ?? $"Section{sectionOrdinal + 1}";
                var sectionType = ident?.Attribute("type")?.Value ?? "section";

                var sourceLang = program.Elements()
                    .Select(c => c.Name.LocalName)
                    .FirstOrDefault(n => n.EndsWith("Source", StringComparison.Ordinal));
                var language = sourceLang != null ? sourceLang[..^"Source".Length] : "";

                var (task, location, order) = assignments.TryGetValue(sectionName, out var a)
                    ? a
                    : ((string?)ident?.Attribute("task")?.Value ?? "MAST", "", 0);

                // A canvas only exists if the FBD parse below actually produces a
                // section for this program (see the HasFbdCanvas update after
                // ParseFbdSource).

                result.Programs.Add(new PlcXmlProgramInfo
                {
                    Name = sectionName,
                    Type = sectionType,
                    Language = language,
                    Task = task,
                    Location = location,
                    Order = order,
                    HasFbdCanvas = false // provisional; corrected after ParseFbdSource
                });
                var programInfo = result.Programs[result.Programs.Count - 1];

                if (!string.Equals(sectionType, "section", StringComparison.OrdinalIgnoreCase))
                {
                    result.SkippedPrograms.Add(new PlcXmlSkippedProgram
                    {
                        Name = sectionName,
                        Reason = $"program type is '{sectionType}', only 'section' programs are imported"
                    });
                    continue;
                }

                var fbd = program.Element("FBDSource");
                if (fbd == null)
                {
                    // A program without any source (an empty section skeleton Unity Pro
                    // emits for every task) is noise — don't surface it. Programs in
                    // other languages (ST, LD, IL, SFC, ...) are real and get listed.
                    if (!string.IsNullOrEmpty(language))
                    {
                        result.SkippedPrograms.Add(new PlcXmlSkippedProgram
                        {
                            Name = sectionName,
                            Reason = $"program is written in {language}, only FBD programs are imported"
                        });
                    }
                    continue;
                }

                var section = ParseFbdSource(fbd, tags, sectionName, task, sectionOrdinal, result);
                sections.Add(section with { Location = location, Order = order });
                sectionOrdinal++;
                programInfo.HasFbdCanvas = true;
            }

            return sections;
        }

        /// <summary>
        /// Reads logicConf: tasks, functional locations, and which task/location
        /// each section is assigned to (with its assignment order).
        /// </summary>
        private static Dictionary<string, (string Task, string Location, int Order)> ParseTaskAssignments(
            XDocument xef, PlcXmlImportResult result)
        {
            var assignments = new Dictionary<string, (string, string, int)>(StringComparer.Ordinal);
            var logicConf = xef.Descendants("logicConf").FirstOrDefault();
            if (logicConf == null)
            {
                result.AddWarning("No <logicConf> found — task/location info unavailable.");
                return assignments;
            }

            // FMId -> location name (Application > Section/Programs folders).
            var fmNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fm in logicConf.Descendants("FMDesc"))
            {
                var id = (string?)fm.Attribute("FMId");
                var name = (string?)fm.Attribute("name");
                if (id != null && name != null && id != "ROOT")
                {
                    fmNames[id] = name;
                    result.Locations.Add(new PlcXmlLocationInfo { Name = name, Id = id });
                }
            }

            foreach (var taskDesc in logicConf.Descendants("taskDesc"))
            {
                var taskName = (string?)taskDesc.Attribute("task") ?? "MAST";
                var task = new PlcXmlTaskInfo
                {
                    Name = taskName,
                    Kind = (string?)taskDesc.Attribute("taskType") ?? "cyclic",
                    MaxExecTimeMs = (int?)taskDesc.Attribute("maxExecTime") ?? 0
                };

                foreach (var sd in taskDesc.Elements("sectionDesc"))
                {
                    var name = (string?)sd.Attribute("name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var fmId = (string?)sd.Attribute("FMId");
                    var location = (string?)sd.Attribute("FMName")
                                   ?? (fmId != null && fmNames.TryGetValue(fmId, out var n) ? n : "");
                    var order = ParseHexInt((string?)sd.Attribute("FMOrder"));

                    assignments[name!] = (taskName, location, order);
                    task.AssignedPrograms.Add(name!);
                }

                result.Tasks.Add(task);
            }

            return assignments;
        }

        private static int ParseHexInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var t = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
            return int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }

        /// <summary>
        /// Reads IOConf into a flat hardware module list (CPU, racks, drops, modules),
        /// keyed by their topological address like "\1.1\1.4".
        /// </summary>
        private static void ParseHardware(XDocument xef, PlcXmlImportResult result)
        {
            var ioConf = xef.Descendants("IOConf").FirstOrDefault();
            if (ioConf == null) return;

            foreach (var part in ioConf.Descendants("partItem"))
            {
                var equip = part.ElementsAfterSelf("equipInfo").FirstOrDefault()
                            ?? part.Parent?.Elements("equipInfo").FirstOrDefault();
                var topo = (string?)equip?.Attribute("topoAddress");
                var partNumber = (string?)part.Attribute("partNumber");
                if (string.IsNullOrEmpty(topo) && string.IsNullOrEmpty(partNumber)) continue;

                result.Hardware.Add(new PlcXmlHardwareModule
                {
                    TopologicalAddress = topo ?? "",
                    Position = (int?)equip?.Attribute("position") ?? 0,
                    Family = (string?)part.Attribute("family") ?? "",
                    PartNumber = partNumber ?? "",
                    Vendor = (string?)part.Attribute("vendorName") ?? "",
                    Version = (string?)part.Attribute("version") ?? ""
                });
            }
        }

        private static PlcXmlSection ParseFbdSource(
            XElement fbdSource,
            Dictionary<string, PlcAddressReference> tags,
            string sectionName,
            string task,
            int sectionOrdinal,
            PlcXmlImportResult result)
        {
            var section = new PlcXmlSection
            {
                Name = sectionName,
                Task = task,
                Index = sectionOrdinal
            };

            // Walk every network in the FBD source. The structure is
            //   FBDSource > networkFBD > FFBBlock / linkFB
            // so we collect all descendants of <networkFBD> under this source.
            var networks = fbdSource.Descendants("networkFBD").ToList();
            if (networks.Count == 0)
                networks = new List<XElement> { fbdSource };



            double networkOrdinalOffset = 0;
            // Legacy placement for blocks with no usable <objPosition>: a flowing
            // 4-row grid advanced per placed node.
            int legacyCursor = 0;

            foreach (var network in networks)
            {
                var blocks = network.Descendants("FFBBlock").ToList();
                var links = network.Descendants("linkFB").ToList();

                // Instance-name → node-id within this network (names are reused
                // across networks, so the map is scoped per network).
                var instanceToNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
                var usedInstances = new HashSet<string>(StringComparer.Ordinal);

                // Measure the widest network so the next one starts below it.
                double maxNodeY = 0;

                foreach (var block in blocks)
                {
                    var rawInstance = (string?)block.Attribute("instanceName") ?? $"n{section.Nodes.Count + 1}";
                    var rawTypeName = (string?)block.Attribute("typeName") ?? "Unknown";
                    // Unity Pro qualifies types as "<LibName>.ADD" — strip the library
                    // prefix so ADD maps even when the library isn't "Basic".
                    var dotIndex = rawTypeName.LastIndexOf('.');
                    var typeName = dotIndex >= 0 ? rawTypeName[(dotIndex + 1)..] : rawTypeName;

                    // De-duplicate within the network (Unity Pro reuses ".1", ".2", ...).
                    var uniqueInstance = rawInstance;
                    var suffix = 2;
                    while (!usedInstances.Add(uniqueInstance))
                        uniqueInstance = $"{rawInstance}_{suffix++}";

                    PlcElementType elementType;
                    PlcElementType? mapped;
                    var hasMapping = TypeMapping.TryGetValue(typeName, out mapped);
                    if (hasMapping && mapped.HasValue)
                    {
                        elementType = mapped.Value;
                    }
                    else
                    {
                        // Mapped to null, or not in the table → Opaque FB.
                        elementType = Opaque;
                        result.UsedOpaqueTypes.Add(typeName);
                    }

                    var nodeId = $"S{sectionOrdinal}_{uniqueInstance}";
                    instanceToNodeId[rawInstance] = nodeId;

                    var node = CreateVisualNode(block, nodeId, uniqueInstance, typeName, elementType, tags);

                    // Unity Pro <objPosition> carries the Control Expert grid-cell
                    // indices: X is in cells of 2 units (a 7-cell-wide TON sits at
                    // 16 then 30, not 23), Y is in cells of 1 unit (5-cell-tall
                    // blocks stack at 170, 175, 180...). Multiply by the cell size
                    // so blocks render at their real relative rows/columns.
                    var (rawX, rawY) = ReadGridPosition(block);
                    var (cellW, cellH) = ReadGridSize(block);
                    if (rawX <= 0 && rawY <= 0)
                    {
                        node.Width = cellW;
                        node.Height = cellH;
                        node.X = LegacyCursorColumn(legacyCursor);
                        node.Y = networkOrdinalOffset + SectionMarginY + GridUnitHeight * LegacyCursorRow(legacyCursor);
                        legacyCursor++;
                    }
                    else
                    {
                        node.Width = cellW;
                        node.Height = cellH;
                        node.X = SectionMarginX + rawX * GridUnitWidth;
                        node.Y = SectionMarginY + networkOrdinalOffset + rawY * GridUnitHeight;
                    }
                    maxNodeY = Math.Max(maxNodeY, node.Y);

                    section.Nodes.Add(node);
                }

                foreach (var link in links)
                {
                    var source = link.Element("linkSource");
                    var dest = link.Element("linkDestination");
                    if (source == null || dest == null) continue;

                    var sourceInstance = (string?)source.Attribute("parentObjectName");
                    var sourcePin = (string?)source.Attribute("pinName");
                    var targetInstance = (string?)dest.Attribute("parentObjectName");
                    var targetPin = (string?)dest.Attribute("pinName");

                    if (string.IsNullOrEmpty(sourceInstance) || string.IsNullOrEmpty(targetInstance)) continue;

                    if (!instanceToNodeId.TryGetValue(sourceInstance, out var sourceNodeId) ||
                        !instanceToNodeId.TryGetValue(targetInstance, out var targetNodeId))
                    {
                        result.AddWarning($"Link references unknown instance: {sourceInstance} -> {targetInstance}");
                        continue;
                    }

                    var sourcePort = NormalizeSourcePort(sourcePin ?? "OUT");
                    var targetPort = NormalizeTargetPort(targetPin ?? "IN");

                    section.Connections.Add(new NodeConnection(sourceNodeId, targetNodeId, targetPort)
                    {
                        SourceConnector = sourcePort
                    });
                }

                // The next network starts below this one.
                networkOrdinalOffset = maxNodeY + 260;
            }

            NormalizeSectionLayout(section);

            if (section.Nodes.Count == 0)
                result.AddWarning($"Section '{sectionName}' ({task}) produced no nodes.");

            return section;
        }

        /// <summary>
        /// Keeps the section's layout as the Control Expert grid placed it; only
        /// compresses the whole bounding box when it exceeds <see cref="MaxSectionSpan"/>
        /// so the editor's scrollable canvas stays usable. Positions are already
        /// margin-based, so no translation is needed.
        /// </summary>
        private static void NormalizeSectionLayout(PlcXmlSection section)
        {
            if (section.Nodes.Count == 0) return;

            var maxRight = section.Nodes.Max(n => n.X + n.Width);
            var maxBottom = section.Nodes.Max(n => n.Y + n.Height);

            var spanX = maxRight - SectionMarginX;
            var spanY = maxBottom - SectionMarginY;
            var overshoot = Math.Max(spanX, spanY);
            if (overshoot <= MaxSectionSpan) return;

            var factor = MaxSectionSpan / overshoot;
            foreach (var node in section.Nodes)
            {
                node.X = SectionMarginX + (node.X - SectionMarginX) * factor;
                node.Y = SectionMarginY + (node.Y - SectionMarginY) * factor;
            }
        }

        // Unity Pro FBD exports place every block with <objPosition posX posY> in
        // Control Expert grid cells. Empirically (GGPLC007 ALARMS): a block at X=16
        // with width=7 ends at 23 and the next sits at 30, so one horizontal cell
        // is 2 units; a block at Y=170 with height=5 ends at 175 and the next sits
        // at 175, so one vertical cell is 1 unit. <FFBBlock> width/height are in
        // the same cell counts. These constants map cells to pixels so the import
        // keeps Control Expert's rows and columns.
        private const double GridUnitWidth = 20;
        private const double GridUnitHeight = 20;

        // A block's <FFBBlock> width/height attributes are in the same grid cells
        // as its position; honouring them reproduces Control Expert's block sizes
        // and keeps adjacent blocks from overlapping. Floor so a 1x1 stub stays
        // clickable.
        private const double MinBlockWidth = 60;
        private const double MinBlockHeight = 40;

        // Fallback grid for blocks whose export carries no usable position:
        // a 4-row flowing column, one block per cell.
        private const int LegacyRows = 4;
        private const double LegacyColumnWidth = 280;

        // Legacy placement keeps a 280px horizontal pitch but must never overlap
        // real grid-placed blocks (which can sit at posX=1: 1*GridUnitWidth).
        private const double LegacyColumnOffset = 2000;

        // Upper bound on a section's pixel span before its layout is compressed;
        // keeps the editor's scrollable canvas usable.
        private const double MaxSectionSpan = 8000;

        // Margins the normalized layout is translated into.
        private const double SectionMarginX = 60;
        private const double SectionMarginY = 40;

        private static double LegacyCursorColumn(int cursor)
            => LegacyColumnOffset + LegacyColumnWidth * (cursor / LegacyRows);

        private static int LegacyCursorRow(int cursor) => cursor % LegacyRows;

        private static (double X, double Y) ReadGridPosition(XElement block)
        {
            double x = 0, y = 0;
            var pos = FindPositionFor(block);
            if (pos != null)
            {
                double.TryParse(pos.Attribute("posX")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                double.TryParse(pos.Attribute("posY")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out y);
            }
            return (x, y);
        }

        // <objPosition> lives at two different depths depending on the export:
        // sometimes directly under <FFBBlock>, sometimes inside an
        // <objectPositions>/<positions> container further down. The old code used
        // a direct child lookup, so every ALARMS block silently fell through to
        // the legacy cursor — blocks stacked in one corner with no wiring visible.
        private static XElement? FindPositionFor(XElement block)
            => block.Descendants("objPosition")
                   .FirstOrDefault(p => p.Attribute("posX") != null && p.Attribute("posY") != null);

        // The <FFBBlock> width/height attributes are in grid cells like the
        // position; use them so the relative proportions match Control Expert.
        private static (double Width, double Height) ReadGridSize(XElement block)
        {
            double w = 0, h = 0;
            double.TryParse(block.Attribute("width")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var aw);
            double.TryParse(block.Attribute("height")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ah);
            if (aw > 0) w = aw * GridUnitWidth;
            if (ah > 0) h = ah * GridUnitHeight;
            return (Math.Max(w, MinBlockWidth), Math.Max(h, MinBlockHeight));
        }

        private static VisualNode CreateVisualNode(
            XElement block,
            string nodeId,
            string uniqueInstance,
            string typeName,
            PlcElementType elementType,
            Dictionary<string, PlcAddressReference> tags)
        {

            var isOpaque = elementType == Opaque;
            var node = new VisualNode
            {
                Id = nodeId,
                // Opaque FBs carry the Unity Pro type in their name so the canvas
                // shows what is being stubbed.
                Name = isOpaque ? $"{uniqueInstance} [{typeName}]" : uniqueInstance,
                ElementType = elementType,
                ShowLiveValues = true
            };

            var description = block.Descendants("descriptionFFB").FirstOrDefault();

            // The REAL Unity Pro pin names (EN/IN/PT/ENO/Q/ET...) become the node's
            // input/output port lists ONLY when they don't collide with the
            // editor's own Input1/Output connector names (Unity's "IN"/"OUT" are
            // different strings, so they land as named rows; "Q" maps to the
            // primary "Output" instead of creating a second, unconnected row).
            if (description != null)
            {
                var outputs = description.Elements("outputVariable")
                    .Select(v => (string?)v.Attribute("formalParameter"))
                    .Where(n => !string.IsNullOrEmpty(n) && !IsEnablePin(n) && !IsPrimaryOutputAlias(n))
                    .Cast<string>()
                    .ToList();
                if (outputs.Count > 0)
                    node.OutputPortNames = new System.Collections.ObjectModel.ObservableCollection<string>(
                        new[] { "Output" }.Concat(outputs));

                var inputs = description.Elements("inputVariable")
                    .Select(v => (string?)v.Attribute("formalParameter"))
                    .Where(n => !string.IsNullOrEmpty(n) && !IsEnablePin(n)
                                && !IsPrimaryInputAlias(n) && !IsSecondaryInputAlias(n))
                    .Cast<string>()
                    .ToList();
                // Seed with Input1/Input2 ONLY for the aliases actually wired, so
                // an AND block shows rows IN1/IN2, not phantom Input1/Input2.
                var rows = new System.Collections.Generic.List<string>();
                if (description.Elements("inputVariable")
                        .Any(v => IsPrimaryInputAlias((string?)v.Attribute("formalParameter"))))
                    rows.Add("Input1");
                if (description.Elements("inputVariable")
                        .Any(v => IsSecondaryInputAlias((string?)v.Attribute("formalParameter"))))
                    rows.Add("Input2");
                rows.AddRange(inputs);
                if (rows.Count > 0)
                    node.InputPortNames = new System.Collections.ObjectModel.ObservableCollection<string>(rows);
            }

            // Bind the primary Modbus addresses from the descriptionFFB pins whose
            // effectiveParameter is a known (addressed) tag.
            if (description != null)
            {
                foreach (var inputVar in description.Elements("inputVariable"))
                {
                    var formal = (string?)inputVar.Attribute("formalParameter");
                    var effective = (string?)inputVar.Attribute("effectiveParameter");
                    if (string.IsNullOrEmpty(formal) || !IsKnownTag(effective, tags)) continue;
                    BindPrimaryInput(node, formal!, effective!, tags);
                }

                foreach (var outputVar in description.Elements("outputVariable"))
                {
                    var formal = (string?)outputVar.Attribute("formalParameter");
                    var effective = (string?)outputVar.Attribute("effectiveParameter");
                    if (string.IsNullOrEmpty(formal) || !IsKnownTag(effective, tags)) continue;
                    BindPrimaryOutput(node, formal!, effective!, tags);
                }
            }

            return node;
        }

        private static bool IsKnownTag(string? effective, Dictionary<string, PlcAddressReference> tags)
        {
            if (string.IsNullOrWhiteSpace(effective)) return false;
            if (ConstantRegex.IsMatch(effective.Trim())) return false;
            return tags.ContainsKey(effective);
        }

        /// <summary>
        /// Binds the primary input address. Unity Pro's first input pin ("IN"/"IN1"/
        /// "START"/"SET"/"S"/"PV") maps to Input1; a distinct second input ("IN2"/"MV")
        /// maps to Input2. EN/ENO are enable pins and are not addresses.
        /// </summary>
        private static void BindPrimaryInput(
            VisualNode node, string formalPin, string tag, Dictionary<string, PlcAddressReference> tags)
        {
            if (!tags.TryGetValue(tag, out var reference)) return;
            var clone = reference.Clone();
            clone.SymbolicName = tag;

            var p = formalPin.Trim().ToUpperInvariant();
            if (p is "EN" or "ENO") return;

            if (p is "IN" or "IN1" or "START" or "SET" or "S" or "R" or "PV")
            {
                if (node.Input1Address is not { Address: >= 0 })
                    node.Input1Address = clone;
                else if (node.Input2Address is not { Address: >= 0 })
                    node.Input2Address = clone;
            }
            else if (p is "IN2" or "MV")
            {
                if (node.Input2Address is not { Address: >= 0 })
                    node.Input2Address = clone;
                else if (node.Input1Address is not { Address: >= 0 })
                    node.Input1Address = clone;
            }
            else
            {
                // Unknown input pin — attach to whichever slot is still free.
                if (node.Input1Address is not { Address: >= 0 })
                    node.Input1Address = clone;
                else if (node.Input2Address is not { Address: >= 0 })
                    node.Input2Address = clone;
            }
        }

        /// <summary>
        /// Binds the primary output address. OUT/OUT1/Q/Y map to the node's
        /// OutputAddress; OUT2/WARN/FAULT to a secondary port binding.
        /// </summary>
        private static void BindPrimaryOutput(
            VisualNode node, string formalPin, string tag, Dictionary<string, PlcAddressReference> tags)
        {
            if (!tags.TryGetValue(tag, out var reference)) return;
            var clone = reference.Clone();
            clone.SymbolicName = tag;

            var p = formalPin.Trim().ToUpperInvariant();
            if (p is "EN" or "ENO") return;

            if (p is "OUT" or "OUT1" or "Q" or "Y")
            {
                node.OutputAddress = clone;
            }
            else
            {
                // Secondary output port (WARN, FAULT, SpeedFeedback, ...).
                if (!node.OutputPortBindings.ContainsKey(p))
                    node.OutputPortBindings[p] = clone;
            }
        }

        /// <summary>EN/ENO are execution-enable pins: not data ports, not wires.</summary>
        private static bool IsEnablePin(string? formalParameter)
            => formalParameter is not null
               && (formalParameter.Equals("EN", StringComparison.OrdinalIgnoreCase)
                   || formalParameter.Equals("ENO", StringComparison.OrdinalIgnoreCase));

        // Unity's "IN"/"IN1"/"SET"/... are the primary data input: they draw on the
        // built-in "Input1" row, so they must not appear as a second named row.
        private static bool IsPrimaryInputAlias(string? pin) => pin != null
            && NormalizeTargetPort(pin) == "Input1";

        private static bool IsSecondaryInputAlias(string? pin) => pin != null
            && NormalizeTargetPort(pin) == "Input2";

        // Likewise "OUT"/"OUT1" draw on the built-in "Output" row.
        private static bool IsPrimaryOutputAlias(string? pin) => pin != null
            && NormalizeSourcePort(pin) == "Output";

        /// <summary>Maps a Unity Pro source pin to a ModbusForge source connector.</summary>
        private static string NormalizeSourcePort(string pin)
        {
            switch (pin.Trim().ToUpperInvariant())
            {
                case "OUT":
                case "OUT1":
                case "Q":
                case "Y":
                case "ENO":
                    return "Output";
                case "OUT2":
                case "WARN":
                case "FAULT":
                case "Q2":
                case "R":
                    return "Output2";
                default:
                    return pin;
            }
        }

        /// <summary>Maps a Unity Pro destination pin to a ModbusForge target connector.</summary>
        private static string NormalizeTargetPort(string pin)
        {
            switch (pin.Trim().ToUpperInvariant())
            {
                case "IN":
                case "IN1":
                case "SET":
                case "START":
                case "PV":
                    return "Input1";
                case "IN2":
                case "MV":
                    return "Input2";
                default:
                    return pin;
            }
        }

        private static XDocument? TryReadXml(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                gz.CopyTo(ms);
                ms.Position = 0;
                return XDocument.Load(ms);
            }
            return XDocument.Load(path);
        }
    }

    // ----------------------------------------------------------------------
    // Result / section models
    // ----------------------------------------------------------------------

    /// <summary>A single imported FBD section as a set of VisualNodes/Connections.</summary>
    public sealed record PlcXmlSection
    {
        public required string Name { get; init; }
        public string Task { get; init; } = "MAST";
        public int Index { get; init; }

        /// <summary>Functional location (Control Expert "location") the section is assigned to.</summary>
        public string Location { get; init; } = "";

        /// <summary>MAST-order of the section within its task assignment.</summary>
        public int Order { get; init; }

        public List<VisualNode> Nodes { get; } = new();
        public List<NodeConnection> Connections { get; } = new();
    }

    /// <summary>A program (section/subprogram) as listed by Control Expert, in any language.</summary>
    public sealed class PlcXmlProgramInfo
    {
        public string Name { get; init; } = "";

        /// <summary>"section" or "subprogram" as per identProgram/@type.</summary>
        public string Type { get; init; } = "section";

        /// <summary>Source language: "FBD", "ST", "LD", "IL", "SFC", ...</summary>
        public string Language { get; init; } = "";

        /// <summary>Task the program is assigned to (from logicConf), if any.</summary>
        public string Task { get; init; } = "MAST";

        /// <summary>Functional location (FMDesc) name it is assigned to, if any.</summary>
        public string Location { get; init; } = "";

        /// <summary>Assignment order inside the task (FMOrder, hex in the XEF).</summary>
        public int Order { get; init; }

        /// <summary>True when the FBD canvas was parsed into nodes.</summary>
        public bool HasFbdCanvas { get; set; }
    }

    /// <summary>A runtime task as shown under Task Configuration.</summary>
    public sealed class PlcXmlTaskInfo
    {
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "cyclic";
        public int MaxExecTimeMs { get; init; }
        public List<string> AssignedPrograms { get; } = new();
    }

    /// <summary>A functional location (Application &gt; Section/Programs folder).</summary>
    public sealed class PlcXmlLocationInfo
    {
        public string Name { get; init; } = "";
        public string Id { get; init; } = "";
    }

    /// <summary>A hardware node from the IOConf tree (rack, CPU, module, ...).</summary>
    public sealed class PlcXmlHardwareModule
    {
        public string TopologicalAddress { get; init; } = "";
        public int Position { get; init; }
        public string Family { get; init; } = "";
        public string PartNumber { get; init; } = "";
        public string Vendor { get; init; } = "";
        public string Version { get; init; } = "";
    }

    public sealed class PlcXmlSkippedProgram
    {
        public string Name { get; init; } = "";
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// Outcome of an XEF import: the parsed sections, tag count, the set of
    /// function-block types preserved as Opaque FBs, and any warnings/errors.
    /// </summary>
    public sealed class PlcXmlImportResult
    {
        public string SourceFile { get; init; } = "";
        public bool Success { get; set; }
        public int TagCount { get; set; }
        public int SectionsFound { get; set; }
        public List<PlcXmlSection> Sections { get; set; } = new();

        /// <summary>Every program in the project (any language), for the PLC navigation tree.</summary>
        public List<PlcXmlProgramInfo> Programs { get; } = new();

        /// <summary>Runtime tasks (MAST, FASTn, ...) with their assignments.</summary>
        public List<PlcXmlTaskInfo> Tasks { get; } = new();

        /// <summary>Functional locations (Application > Section/Programs folders).</summary>
        public List<PlcXmlLocationInfo> Locations { get; } = new();

        /// <summary>Hardware modules from IOConf (CPU, racks, DI/DO/AI/AO modules, ...).</summary>
        public List<PlcXmlHardwareModule> Hardware { get; } = new();
        public HashSet<string> UsedOpaqueTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PlcXmlSkippedProgram> SkippedPrograms { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();

        public int TotalNodes => Sections.Sum(s => s.Nodes.Count);
        public int TotalConnections => Sections.Sum(s => s.Connections.Count);

        public static PlcXmlImportResult Failure(string message)
        {
            var r = new PlcXmlImportResult { Success = false };
            r.Errors.Add(message);
            return r;
        }

        public void AddWarning(string message) => Warnings.Add(message);
        public void AddError(string message) => Errors.Add(message);
    }
}


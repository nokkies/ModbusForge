using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModbusForge.Core.Xef
{
    /// <summary>
    /// Parses a Schneider Unity/ControlExpert .XEF (or .ZEF) file into an <see cref="XefProject"/>.
    ///
    /// The XEF payload is plain XML (a .ZEF is a ZIP whose member is that XML; a bare .XEF may be
    /// the XML itself). This parser is deliberately tolerant: it locates the root by content
    /// (contentHeader / program / dataBlock) rather than a fixed root element name, so it works
    /// whether the top-level element is the documented name or an undocumented wrapper.
    /// </summary>
    public static class XefParser
    {
        /// <summary>
        /// Loads and parses an XEF from disk. Handles both the bare-XML and zip-wrapped forms.
        /// </summary>
        public static XefProject ParseFile(string path, ILogger? logger = null)
        {
            var log = logger ?? NullLogger.Instance;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("XEF file not found.", path);
            }

            var root = LoadRootElement(path, log);
            log.LogInformation("Loaded XEF XML root '<{Root}>' from {Path}", root.Name.LocalName, path);

            var project = Parse(root);
            project.SourcePath = path;
            return project;
        }

        /// <summary>Parses an already-loaded XEF XML root element (for tests and embedded callers).</summary>
        public static XefProject Parse(XElement root, ILogger? logger = null)
        {
            var log = logger ?? NullLogger.Instance;
            var project = new XefProject();

            var header = root.Element("contentHeader") ?? Descendant("contentHeader", root);
            if (header != null)
            {
                project.Name = header.Attribute("name")?.Value ?? "XEF";
            }
            else
            {
                project.Name = "XEF";
                project.Warnings.Add("No <contentHeader> found; project name unknown.");
            }

            ParseTasks(root, project);
            ParseVariables(root, project, log);
            ParseDataTypes(root, project);
            ParseBlockTypes(root, project);
            ParsePrograms(root, project, log);

            return project;
        }

        // ---- element location helpers (tolerant to nesting depth / namespace) ----

        private static IEnumerable<XElement> Descendants(XElement root, string localName)
            => root.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

        private static XElement? Descendant(string localName, XElement root)
            => Descendants(root, localName).FirstOrDefault();

        // ---- tasks / sections ----

        private static void ParseTasks(XElement root, XefProject project)
        {
            foreach (var section in Descendants(root, "sectionDesc"))
            {
                project.Tasks.Add(new XefTask
                {
                    Name = section.Attribute("name")?.Value ?? string.Empty,
                    FMName = section.Attribute("FMName")?.Value ?? string.Empty,
                    FMId = section.Attribute("FMId")?.Value ?? string.Empty,
                    FMOrder = section.Attribute("FMOrder")?.Value ?? string.Empty,
                    SectionOrder = int.TryParse(section.Attribute("SectionOrder")?.Value, out var so) ? so : 0,
                });
            }

            // Keep tasks in execution order (SectionOrder, then FMOrder as a string tie-break).
            project.Tasks.Sort((a, b) =>
                a.SectionOrder != b.SectionOrder
                    ? a.SectionOrder.CompareTo(b.SectionOrder)
                    : string.CompareOrdinal(a.FMOrder, b.FMOrder));
        }

        // ---- variables / IO table ----

        private static void ParseVariables(XElement root, XefProject project, ILogger log)
        {
            var dataBlock = root.Element("dataBlock") ?? Descendant("dataBlock", root);
            if (dataBlock == null)
            {
                project.Warnings.Add("No <dataBlock> found; the variable/IO table is empty.");
                return;
            }

            foreach (var variable in Descendants(dataBlock, "variables"))
            {
                var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var inst in Descendants(variable, "instanceElementDesc"))
                {
                    var value = inst.Element("value");
                    if (value != null)
                    {
                        members[inst.Attribute("name")?.Value ?? string.Empty] = value.Value;
                    }
                }

                var init = variable.Element("variableInit");
                var comment = variable.Element("comment");

                var variableModel = new XefVariable
                {
                    Name = variable.Attribute("name")?.Value ?? string.Empty,
                    DataType = variable.Attribute("typeName")?.Value ?? string.Empty,
                    Address = variable.Attribute("topologicalAddress")?.Value,
                    Value = init?.Attribute("value")?.Value,
                    Comment = comment?.Value,
                };

                variableModel.Members = members;
                project.Variables.Add(variableModel);
            }

            log.LogInformation("Parsed {Count} variables from <dataBlock>", project.Variables.Count);
        }

        // ---- user data types ----

        private static void ParseDataTypes(XElement root, XefProject project)
        {
            foreach (var ddt in Descendants(root, "DDTSource"))
            {
                var dt = new XefDataType
                {
                    Name = ddt.Attribute("DDTName")?.Value ?? string.Empty,
                };

                var structure = ddt.Element("structure") ?? Descendant("structure", ddt);
                if (structure != null)
                {
                    foreach (var member in Descendants(structure, "variables"))
                    {
                        dt.Members.Add(new XefDataTypeMember
                        {
                            Name = member.Attribute("name")?.Value ?? string.Empty,
                            DataType = member.Attribute("typeName")?.Value ?? string.Empty,
                            Comment = member.Element("comment")?.Value,
                        });
                    }
                }

                project.DataTypes.Add(dt);
            }
        }

        // ---- function-block type signatures ----

        private static void ParseBlockTypes(XElement root, XefProject project)
        {
            foreach (var kind in new[] { "FBSource", "EFSource", "EFBSource" })
            {
                foreach (var source in Descendants(root, kind))
                {
                    // The type name attribute is "nameOf" + <KindWithoutSource> + "Type",
                    // e.g. FBSource -> nameOfFBType, EFSource -> nameOfEFType.
                    var nameAttr = "nameOf" + kind[..^6] + "Type"; // strip "Source"
                    var name = source.Attribute(nameAttr)?.Value ?? string.Empty;

                    var bt = new XefBlockType
                    {
                        Name = name,
                        SourceKind = kind[..^6],
                    };

                    AddPins(source, "inputParameters", "i", bt.Inputs);
                    AddPins(source, "outputParameters", "o", bt.Outputs);
                    AddPins(source, "inOutParameters", "io", bt.InOuts);

                    // The XEF duplicates parameter lists under <ExternalToolsOnly>; avoid double-adding.
                    project.BlockTypes.Add(bt);
                }
            }
        }

        private static void AddPins(XElement source, string parameterList, string direction, List<XefBlockTypePin> into)
        {
            // Collect every top-level parameter list of this name (some XEFs repeat the element;
            // others keep one list per direction). Skip the ExternalToolsOnly duplicate.
            foreach (var list in source.Elements().Where(e => e.Name.LocalName == parameterList))
            {
                var parent = list.Parent;
                if (parent != null && parent.Name.LocalName == "ExternalToolsOnly")
                {
                    continue;
                }

                foreach (var variable in Descendants(list, "variables"))
                {
                    // Avoid adding the same pin twice if the list is repeated.
                    if (into.Any(p => string.Equals(p.Name, variable.Attribute("name")?.Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    into.Add(new XefBlockTypePin
                    {
                        Name = variable.Attribute("name")?.Value ?? string.Empty,
                        Type = variable.Attribute("typeName")?.Value ?? string.Empty,
                        Direction = direction,
                    });
                }
            }
        }

        // ---- programs ----

        private static void ParsePrograms(XElement root, XefProject project, ILogger log)
        {
            foreach (var programEl in Descendants(root, "program"))
            {
                var ident = programEl.Element("identProgram") ?? Descendant("identProgram", programEl);
                var name = ident?.Attribute("name")?.Value ?? string.Empty;

                var program = new XefProgram { Name = name, Language = "FBD" };

                // FBD side
                var fbd = programEl.Element("FBDSource") ?? Descendant("FBDSource", programEl);
                if (fbd != null)
                {
                    var network = fbd.Element("networkFBD") ?? Descendant("networkFBD", fbd);
                    if (network != null)
                    {
                        foreach (var blockEl in network.Elements().Where(e => e.Name.LocalName == "FFBBlock"))
                        {
                            program.Blocks.Add(ParseBlock(blockEl, project));
                        }

                        foreach (var linkEl in network.Elements().Where(e => e.Name.LocalName == "linkFB"))
                        {
                            program.Links.Add(ParseLink(linkEl));
                        }

                        foreach (var textEl in network.Elements().Where(e => e.Name.LocalName == "textBox"))
                        {
                            program.Comments.Add(ParseComment(textEl));
                        }
                    }
                }

                // Ladder side — a program with a ladder source (LADSource / networkLAD rungs)
                // is rendered as LD. Checked before ST so an LD program is not mislabelled.
                var rungs = ParseLadder(programEl);
                if (rungs.Count > 0)
                {
                    program.Language = "LD";
                    program.Rungs.AddRange(rungs);
                    project.HasLadderPrograms = true;
                }

                // ST side — look for an ST source element. The exact element name is not yet
                // confirmed by a real ST-heavy XEF, so probe the common names and report honestly.
                var st = FindStSource(programEl);
                if (st != null)
                {
                    program.Language = "ST";
                    program.StSource = st;
                    project.HasStPrograms = true;
                }
                else if (program.Blocks.Count == 0 && program.Rungs.Count == 0)
                {
                    // No FBD blocks and no recognisable ST source: flag it so the UI can show
                    // "unrecognised program" rather than silently pretending it's empty.
                    project.Warnings.Add($"Program '{name}' had no FBD blocks and no recognised ST source; it may be an unhandled language.");
                }

                project.Programs.Add(program);
                log.LogInformation("Parsed program '{Name}': {Lang}, {Blocks} blocks, {Links} links",
                    name, program.Language, program.Blocks.Count, program.Links.Count);
            }
        }

        private static XefBlock ParseBlock(XElement blockEl, XefProject project)
        {
            var position = blockEl.Element("objPosition") ?? Descendant("objPosition", blockEl);
            var block = new XefBlock
            {
                InstanceName = blockEl.Attribute("instanceName")?.Value ?? string.Empty,
                TypeName = blockEl.Attribute("typeName")?.Value ?? string.Empty,
                EnEno = string.Equals(blockEl.Attribute("enEnO")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                PosX = int.TryParse(position?.Attribute("posX")?.Value, out var px) ? px : 0,
                PosY = int.TryParse(position?.Attribute("posY")?.Value, out var py) ? py : 0,
                Width = int.TryParse(blockEl.Attribute("width")?.Value, out var bw) ? bw : 4,
                Height = int.TryParse(blockEl.Attribute("height")?.Value, out var bh) ? bh : 3,
            };

            foreach (var input in blockEl.Descendants().Where(e => e.Name.LocalName == "inputVariable"))
            {
                block.Inputs.Add(ParsePin(input));
            }

            foreach (var output in blockEl.Descendants().Where(e => e.Name.LocalName == "outputVariable"))
            {
                block.Outputs.Add(ParsePin(output));
            }

            // An FBD block whose typeName is not a known function block (and not a user AOI)
            // is surfaced as a warning so the UI can mark it "no simulation equivalent".
            if (project.BlockTypes.Count > 0 &&
                !project.BlockTypes.Any(t => string.Equals(t.Name, block.TypeName, StringComparison.OrdinalIgnoreCase)))
            {
                // Only warn once per unknown type to avoid flooding for many instances.
                if (!project.Warnings.Any(w => w.StartsWith("Unknown block type '")))
                {
                    project.Warnings.Add($"Unknown block type '{block.TypeName}' (not in the XEF's FB/EF source list).");
                }
            }

            return block;
        }

        private static XefPin ParsePin(XElement pinEl)
        {
            var effective = pinEl.Attribute("effectiveParameter")?.Value;
            return new XefPin
            {
                FormalParameter = pinEl.Attribute("formalParameter")?.Value ?? string.Empty,
                EffectiveParameter = string.IsNullOrWhiteSpace(effective) ? null : effective,
                Inverted = string.Equals(pinEl.Attribute("invertedPin")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                HasEquation = LooksLikeEquation(effective),
            };
        }

        private static XefLink ParseLink(XElement linkEl)
        {
            var src = linkEl.Element("linkSource");
            var dst = linkEl.Element("linkDestination");
            return new XefLink
            {
                SourceBlock = src?.Attribute("parentObjectName")?.Value ?? string.Empty,
                SourcePin = src?.Attribute("pinName")?.Value ?? string.Empty,
                DestBlock = dst?.Attribute("parentObjectName")?.Value ?? string.Empty,
                DestPin = dst?.Attribute("pinName")?.Value ?? string.Empty,
            };
        }

        private static XefComment ParseComment(XElement textEl)
        {
            var position = textEl.Element("objPosition") ?? Descendant("objPosition", textEl);
            return new XefComment
            {
                X = int.TryParse(position?.Attribute("posX")?.Value, out var x) ? x : 0,
                Y = int.TryParse(position?.Attribute("posY")?.Value, out var y) ? y : 0,
                Width = int.TryParse(textEl.Attribute("width")?.Value, out var w) ? w : 0,
                Height = int.TryParse(textEl.Attribute("height")?.Value, out var h) ? h : 0,
                Text = textEl.Value.Trim(),
            };
        }

        /// <summary>
        /// Probes a program element for an ST source. The exact element name is not yet
        /// confirmed, so this checks the common candidates and returns the text if found.
        /// Returns null when no ST source is recognisable (the caller then treats the program
        /// as FBD or, if it has no blocks, raises a warning).
        /// </summary>
        private static string? FindStSource(XElement programEl)
        {
            foreach (var candidate in new[] { "STSource", "SourceST", "ST", "StructuredText", "STProgram", "networkST" })
            {
                var el = programEl.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, candidate, StringComparison.OrdinalIgnoreCase))
                       ?? Descendant(candidate, programEl);
                if (el != null && !string.IsNullOrWhiteSpace(el.Value))
                {
                    return el.Value;
                }
            }

            return null;
        }

        private static bool LooksLikeEquation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // A pure number (int/float) or a single identifier is not an equation; the presence
            // of an operator is. This mirrors the reference parser's heuristic.
            var operators = new[] { '+', '-', '*', '/', '<', '>', '=' };
            return operators.Any(value.Contains);
        }

        /// <summary>
        /// Parses a program's ladder (LD) source into rungs. Looks for a ladder container
        /// (LADSource / LadderSource / LDSource) holding networkLAD rungs; each rung holds
        /// elements (contacts, coils, set/reset coils, or inline blocks). Returns an empty
        /// list when the program has no ladder source.
        /// </summary>
        private static List<XefLadderRung> ParseLadder(XElement programEl)
        {
            var result = new List<XefLadderRung>();

            // Find the ladder container (the FBD analog is FBDSource; the ladder analog is
            // LADSource). Accept the common candidates so a real Schneider LD XEF works.
            var lad = programEl.Elements()
                .FirstOrDefault(e => e.Name.LocalName is "LADSource" or "LadderSource" or "LDSource")
                ?? Descendant("LADSource", programEl)
                ?? Descendant("LadderSource", programEl)
                ?? Descendant("LDSource", programEl);
            if (lad == null)
            {
                return result;
            }

            int index = 0;
            foreach (var rungEl in lad.Descendants().Where(e => e.Name.LocalName == "networkLAD"))
            {
                var rung = new XefLadderRung { Index = index++ };
                foreach (var el in rungEl.Elements())
                {
                    var element = ParseLadderElement(el);
                    if (element != null)
                    {
                        rung.Elements.Add(element);
                    }
                }
                result.Add(rung);
            }

            return result;
        }

        private static XefLadderElement? ParseLadderElement(XElement el)
        {
            var name = el.Name.LocalName.ToLowerInvariant();
            var tag = el.Attribute("variable")?.Value
                      ?? el.Attribute("symbol")?.Value
                      ?? el.Attribute("name")?.Value
                      ?? el.Attribute("tag")?.Value
                      ?? string.Empty;
            var col = int.TryParse(el.Attribute("col")?.Value ?? el.Attribute("x")?.Value, out var c) ? c : 0;
            var row = int.TryParse(el.Attribute("row")?.Value ?? el.Attribute("y")?.Value, out var r) ? r : 0;
            var blockType = el.Attribute("typeName")?.Value ?? string.Empty;

            switch (name)
            {
                case "contact":
                case "ldcontact":
                    return new XefLadderElement
                    {
                        Kind = string.Equals(el.Attribute("negated")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                               ? LadderElementKind.ContactNegated
                               : LadderElementKind.Contact,
                        Tag = tag, Col = col, Row = row,
                    };

                case "risecontact":
                case "contactrise":
                    return new XefLadderElement { Kind = LadderElementKind.ContactRise, Tag = tag, Col = col, Row = row };

                case "fallcontact":
                case "contactfall":
                    return new XefLadderElement { Kind = LadderElementKind.ContactFall, Tag = tag, Col = col, Row = row };

                case "coil":
                    return new XefLadderElement
                    {
                        Kind = string.Equals(el.Attribute("negated")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                               ? LadderElementKind.CoilNegated
                               : LadderElementKind.Coil,
                        Tag = tag, Col = col, Row = row,
                    };

                case "setcoil":
                    return new XefLadderElement { Kind = LadderElementKind.CoilSet, Tag = tag, Col = col, Row = row };

                case "resetcoil":
                case "rstcoil":
                    return new XefLadderElement { Kind = LadderElementKind.CoilReset, Tag = tag, Col = col, Row = row };

                case "block":
                case "fbdblock":
                case "inlinblock":
                    return string.IsNullOrEmpty(blockType)
                        ? null
                        : new XefLadderElement { Kind = LadderElementKind.Coil, Tag = tag, BlockType = blockType, Col = col, Row = row };

                default:
                    return null;
            }
        }

        // ---- root loading (bare XML vs zip-wrapped) ----

        private static XElement LoadRootElement(string path, ILogger log)
        {
            var bytes = File.ReadAllBytes(path);

            // ZIP magic: PK\x03\x04
            if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            {
                log.LogInformation("XEF is a ZIP container; extracting XML member");
                using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var entry = archive.Entries
                    .FirstOrDefault(e => e.Name.EndsWith(".xef", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
                    ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
                    ?? archive.Entries.FirstOrDefault(e => e.Length > 0);

                if (entry == null)
                {
                    throw new InvalidDataException("ZIP container has no usable XML member.");
                }

                using var stream = entry.Open();
                return LoadXmlFromStream(stream);
            }

            // Otherwise treat the whole file as XML.
            using var fileStream = File.OpenRead(path);
            return LoadXmlFromStream(fileStream);
        }

        private static XElement LoadXmlFromStream(Stream stream)
        {
            try
            {
                // XDocument handles CDATA / mixed content and is namespace-agnostic for our
                // LocalName-based traversal.
                var doc = XDocument.Load(stream, LoadOptions.None);
                return doc.Root ?? throw new InvalidDataException("XML document has no root element.");
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException(
                    "The file is not well-formed XML (and is not a recognisable ZIP-wrapped XEF). " +
                    "Export the application from Control Expert as .XEF/.ZEF and retry.", ex);
            }
        }
    }
}

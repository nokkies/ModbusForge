using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Service for importing and exporting IEC 61131-3 XEF (XML Exchange Format) files
    /// </summary>
    public interface IXefService
    {
        Task<XefProject?> ImportXefAsync(string filePath);
        Task ExportXefAsync(XefProject project, string filePath);
        Task<VisualNodeEditorConfig> ConvertXefToVisualNodesAsync(XefProject xefProject);
        Task<XefProject> ConvertVisualNodesToXefAsync(VisualNodeEditorConfig config, string projectName);
        Task<LadderRung[]> ConvertVisualNodesToLadderAsync(VisualNodeEditorConfig config);
        Task<SfcChart?> ConvertVisualNodesToSfcAsync(VisualNodeEditorConfig config);
    }

    /// <summary>
    /// Implementation of XEF import/export service for IEC 61131-3 compliance
    /// </summary>
    public class XefService : IXefService
    {
        private readonly ILogger<XefService> _logger;

        public XefService(ILogger<XefService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Import XEF file from disk
        /// </summary>
        public async Task<XefProject?> ImportXefAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogError("XEF file not found: {FilePath}", filePath);
                    return null;
                }

                var xml = await File.ReadAllTextAsync(filePath);
                
                // Try parsing as XML (IEC 61131-3 format)
                if (xml.TrimStart().StartsWith("<?xml") || xml.TrimStart().StartsWith("<"))
                {
                    return ParseXefXml(xml);
                }
                
                // Try parsing as JSON (alternative format)
                if (xml.TrimStart().StartsWith("{"))
                {
                    return JsonSerializer.Deserialize<XefProject>(xml);
                }

                _logger.LogError("Unable to parse XEF file: {FilePath}", filePath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import XEF file: {FilePath}", filePath);
                return null;
            }
        }

        /// <summary>
        /// Export XEF project to disk
        /// </summary>
        public async Task ExportXefAsync(XefProject project, string filePath)
        {
            try
            {
                project.ProjectInfo.Modified = DateTime.Now;
                
                // Export as XML (IEC 61131-3 standard format)
                var xml = GenerateXefXml(project);
                await File.WriteAllTextAsync(filePath, xml);
                
                _logger.LogInformation("Exported XEF project to {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export XEF file: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// Parse XEF XML format
        /// </summary>
        private XefProject ParseXefXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            var project = new XefProject();

            var root = doc.Element("Project") ?? doc.Element("IEC61131Project");
            if (root == null)
            {
                throw new InvalidOperationException("Invalid XEF format: missing Project element");
            }

            // Parse project info
            var projectInfo = root.Element("ProjectInfo");
            if (projectInfo != null)
            {
                project.ProjectInfo.Name = GetElementValue(projectInfo, "Name", "Untitled");
                project.ProjectInfo.Description = GetElementValue(projectInfo, "Description", "");
                project.ProjectInfo.Vendor = GetElementValue(projectInfo, "Vendor", "ModbusForge");
            }

            // Parse global variables
            var globalVars = root.Element("GlobalVariables") ?? root.Element("VarList");
            if (globalVars != null)
            {
                foreach (var varElem in globalVars.Elements("Variable").Concat(globalVars.Elements("Var")))
                {
                    var tag = ParsePlcTag(varElem);
                    if (tag != null)
                        project.GlobalVariables.Add(tag);
                }
            }

            // Parse POUs
            var pous = root.Element("POUs") ?? root.Element("Programs");
            if (pous != null)
            {
                foreach (var pouElem in pous.Elements("POU").Concat(pous.Elements("Program")).Concat(pous.Elements("FunctionBlock")))
                {
                    var pou = ParsePou(pouElem);
                    if (pou != null)
                        project.Pous.Add(pou);
                }
            }

            return project;
        }

        /// <summary>
        /// Parse a PLC tag from XML
        /// </summary>
        private PlcTag? ParsePlcTag(XElement elem)
        {
            var tag = new PlcTag
            {
                Name = GetAttribute(elem, "Name") ?? GetElementValue(elem, "Name", ""),
                Comment = GetElementValue(elem, "Comment", ""),
            };

            // Parse data type
            var dataTypeStr = GetAttribute(elem, "Type") ?? GetElementValue(elem, "Type", "BOOL");
            if (Enum.TryParse<IecDataType>(dataTypeStr.ToUpper(), out var dataType))
                tag.DataType = dataType;

            // Parse address
            var address = GetElementValue(elem, "Address", "");
            if (!string.IsNullOrEmpty(address) && ParseModbusAddress(address, out var area, out var addr))
            {
                tag.Area = area;
                tag.Address = addr;
            }

            return tag;
        }

        /// <summary>
        /// Parse a POU from XML
        /// </summary>
        private Pou? ParsePou(XElement elem)
        {
            var pou = new Pou
            {
                Name = GetAttribute(elem, "Name") ?? GetElementValue(elem, "Name", ""),
                Comment = GetElementValue(elem, "Comment", ""),
            };

            // Parse type
            var typeStr = GetAttribute(elem, "Type") ?? elem.Name.LocalName;
            if (Enum.TryParse<PouType>(typeStr, true, out var pouType))
                pou.Type = pouType;

            // Parse implementation language
            var langStr = GetAttribute(elem, "Language") ?? GetElementValue(elem, "Language", "FBD");
            pou.ImplementationLanguage = langStr;

            // Parse local variables
            var localVars = elem.Element("LocalVariables") ?? elem.Element("VarList");
            if (localVars != null)
            {
                foreach (var varElem in localVars.Elements())
                {
                    var tag = ParsePlcTag(varElem);
                    if (tag != null)
                        pou.LocalTags.Add(tag);
                }
            }

            // Parse implementation based on language
            switch (pou.ImplementationLanguage.ToUpper())
            {
                case "LD":
                case "LADDER":
                    pou.LadderImplementation = ParseLadderFromXml(elem);
                    break;
                case "SFC":
                    pou.SfcImplementation = ParseSfcFromXml(elem);
                    break;
                case "ST":
                    pou.StImplementation = GetElementValue(elem, "ST") ?? GetElementValue(elem, "StructuredText", "");
                    break;
                case "FBD":
                default:
                    pou.FbdImplementation = ParseFbdFromXml(elem);
                    break;
            }

            return pou;
        }

        /// <summary>
        /// Parse ladder diagram from XML
        /// </summary>
        private ObservableCollection<LadderRung> ParseLadderFromXml(XElement pouElem)
        {
            var rungs = new ObservableCollection<LadderRung>();
            var networkList = pouElem.Element("NetworkList") ?? pouElem.Element("Rungs") ?? pouElem.Element("Body");
            
            if (networkList == null)
                return rungs;

            int rungNum = 0;
            foreach (var networkElem in networkList.Elements())
            {
                var rung = new LadderRung
                {
                    RungNumber = ++rungNum,
                    Comment = GetElementValue(networkElem, "Comment", "")
                };

                // Parse contacts (left side)
                var leftSide = networkElem.Element("Left") ?? networkElem.Element("Conditions");
                if (leftSide != null)
                {
                    foreach (var contactElem in leftSide.Descendants("Contact").Concat(leftSide.Descendants("Connector")))
                    {
                        var contact = ParseContactElement(contactElem);
                        if (contact != null)
                            rung.Elements.Add(contact);
                    }
                }

                // Parse coil (right side)
                var rightSide = networkElem.Element("Right") ?? networkElem.Element("Outputs");
                if (rightSide != null)
                {
                    var coilElem = rightSide.Element("Coil") ?? rightSide.Element("Assignment");
                    if (coilElem != null)
                    {
                        rung.Coil = ParseCoilElement(coilElem);
                    }
                }

                rungs.Add(rung);
            }

            return rungs;
        }

        /// <summary>
        /// Parse contact element from XML
        /// </summary>
        private ContactElement? ParseContactElement(XElement elem)
        {
            var contact = new ContactElement
            {
                Address = GetAttribute(elem, "Name") ?? GetElementValue(elem, "Name", ""),
                NormallyClosed = GetAttribute(elem, "Negated") == "true" || 
                                GetAttribute(elem, "NormallyClosed") == "true"
            };

            var typeStr = GetAttribute(elem, "Type") ?? "Input";
            if (Enum.TryParse<PlcElementType>(typeStr, true, out var type))
                contact.ElementType = type;

            return contact;
        }

        /// <summary>
        /// Parse coil element from XML
        /// </summary>
        private CoilElement? ParseCoilElement(XElement elem)
        {
            var coil = new CoilElement
            {
                Address = GetAttribute(elem, "Name") ?? GetElementValue(elem, "Name", ""),
                Negated = GetAttribute(elem, "Negated") == "true",
                SetReset = GetAttribute(elem, "SetReset") == "true",
                IsSetCoil = GetAttribute(elem, "IsSet") == "true"
            };

            return coil;
        }

        /// <summary>
        /// Parse SFC chart from XML
        /// </summary>
        private SfcChart? ParseSfcFromXml(XElement pouElem)
        {
            var sfc = new SfcChart();
            var sfcElem = pouElem.Element("SFC") ?? pouElem.Element("SequentialFunctionChart");
            
            if (sfcElem == null)
                return null;

            // Parse steps
            var steps = sfcElem.Element("Steps");
            if (steps != null)
            {
                foreach (var stepElem in steps.Elements("Step"))
                {
                    var step = new SfcStep
                    {
                        Name = GetAttribute(stepElem, "Name") ?? GetElementValue(stepElem, "Name", ""),
                        IsInitial = GetAttribute(stepElem, "Initial") == "true"
                    };

                    // Parse actions
                    var actions = stepElem.Element("Actions");
                    if (actions != null)
                    {
                        foreach (var actionElem in actions.Elements("Action"))
                        {
                            var action = new SfcAction
                            {
                                Name = GetElementValue(actionElem, "Name", ""),
                                Qualifier = ParseActionQualifier(GetElementValue(actionElem, "Qualifier", "N"))
                            };
                            step.Actions.Add(action);
                        }
                    }

                    sfc.Steps.Add(step);
                }
            }

            // Parse transitions
            var transitions = sfcElem.Element("Transitions");
            if (transitions != null)
            {
                foreach (var transElem in transitions.Elements("Transition"))
                {
                    var transition = new SfcTransition
                    {
                        Condition = GetElementValue(transElem, "Condition", ""),
                        SourceStepId = GetAttribute(transElem, "Source") ?? "",
                        TargetStepId = GetAttribute(transElem, "Target") ?? ""
                    };
                    sfc.Transitions.Add(transition);
                }
            }

            // Find initial step
            sfc.InitialStepId = sfc.Steps.FirstOrDefault(s => s.IsInitial)?.Id ?? sfc.Steps.FirstOrDefault()?.Id;

            return sfc;
        }

        /// <summary>
        /// Parse action qualifier from string
        /// </summary>
        private SfcActionQualifier ParseActionQualifier(string qualifier)
        {
            return qualifier.ToUpper() switch
            {
                "N" => SfcActionQualifier.NonStored,
                "S" => SfcActionQualifier.Stored,
                "R" => SfcActionQualifier.Reset,
                "P" => SfcActionQualifier.LeadingEdge,
                "M" => SfcActionQualifier.TrailingEdge,
                "D" => SfcActionQualifier.Delayed,
                "L" => SfcActionQualifier.Limited,
                _ => SfcActionQualifier.NonStored
            };
        }

        /// <summary>
        /// Parse FBD from XML
        /// </summary>
        private VisualNodeEditorConfig? ParseFbdFromXml(XElement pouElem)
        {
            var config = new VisualNodeEditorConfig();
            var body = pouElem.Element("Body") ?? pouElem.Element("FBD");
            
            if (body == null)
                return config;

            // Parse networks/blocks
            var networks = body.Element("NetworkList") ?? body.Element("Blocks");
            if (networks != null)
            {
                foreach (var netElem in networks.Elements())
                {
                    // Parse blocks as visual nodes
                    foreach (var blockElem in netElem.Descendants("Block"))
                    {
                        var node = ParseVisualNode(blockElem);
                        if (node != null)
                            config.Nodes.Add(node);
                    }

                    // Parse connections
                    foreach (var connElem in netElem.Descendants("Connection").Concat(netElem.Descendants("Link")))
                    {
                        var connection = ParseNodeConnection(connElem);
                        if (connection != null)
                            config.Connections.Add(connection);
                    }
                }
            }

            return config;
        }

        /// <summary>
        /// Parse visual node from XML
        /// </summary>
        private VisualNode? ParseVisualNode(XElement elem)
        {
            var node = new VisualNode
            {
                Name = GetAttribute(elem, "Name") ?? GetElementValue(elem, "Name", "Block"),
                X = double.Parse(GetAttribute(elem, "X") ?? "100"),
                Y = double.Parse(GetAttribute(elem, "Y") ?? "100"),
                Width = double.Parse(GetAttribute(elem, "Width") ?? "240"),
                Height = double.Parse(GetAttribute(elem, "Height") ?? "140")
            };

            var typeStr = GetAttribute(elem, "Type") ?? GetElementValue(elem, "Type", "AND");
            if (Enum.TryParse<PlcElementType>(typeStr, true, out var type))
                node.ElementType = type;

            // Parse input/output addresses
            var inputAddr = GetElementValue(elem, "InputAddress", "");
            if (!string.IsNullOrEmpty(inputAddr) && ParseModbusAddress(inputAddr, out var area, out var addr))
            {
                node.Input1Address = new PlcAddressReference { Area = area, Address = addr };
            }

            var outputAddr = GetElementValue(elem, "OutputAddress", "");
            if (!string.IsNullOrEmpty(outputAddr) && ParseModbusAddress(outputAddr, out area, out addr))
            {
                node.OutputAddress = new PlcAddressReference { Area = area, Address = addr };
            }

            return node;
        }

        /// <summary>
        /// Parse node connection from XML
        /// </summary>
        private NodeConnection? ParseNodeConnection(XElement elem)
        {
            var sourceId = GetAttribute(elem, "SourceBlock") ?? GetAttribute(elem, "Source");
            var targetId = GetAttribute(elem, "TargetBlock") ?? GetAttribute(elem, "Target");

            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
                return null;

            return new NodeConnection(sourceId, targetId)
            {
                SourceConnector = GetAttribute(elem, "SourceConn") ?? "Output",
                TargetConnector = GetAttribute(elem, "TargetConn") ?? "Input1"
            };
        }

        /// <summary>
        /// Generate XEF XML from project
        /// </summary>
        private string GenerateXefXml(XefProject project)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement("Project",
                    new XAttribute("xmlns", "http://www.iec.ch/61131"),
                    new XAttribute("version", project.Version),
                    GenerateProjectInfoXml(project.ProjectInfo),
                    GenerateGlobalVariablesXml(project.GlobalVariables),
                    GeneratePousXml(project.Pous),
                    GenerateConfigurationsXml(project.Configurations)
                )
            );

            using var writer = new StringWriter();
            doc.Save(writer);
            return writer.ToString();
        }

        /// <summary>
        /// Generate project info XML
        /// </summary>
        private XElement GenerateProjectInfoXml(XefProjectInfo info)
        {
            return new XElement("ProjectInfo",
                new XElement("Name", info.Name),
                new XElement("Description", info.Description),
                new XElement("Vendor", info.Vendor),
                new XElement("Created", info.Created.ToString("o")),
                new XElement("Modified", info.Modified.ToString("o")),
                new XElement("IecVersion", info.IecVersion)
            );
        }

        /// <summary>
        /// Generate global variables XML
        /// </summary>
        private XElement GenerateGlobalVariablesXml(IEnumerable<PlcTag> variables)
        {
            return new XElement("GlobalVariables",
                variables.Select(v => new XElement("Variable",
                    new XAttribute("Name", v.Name),
                    new XAttribute("Type", v.DataType.ToString()),
                    new XAttribute("Address", $"{v.Area}:{v.Address}"),
                    new XElement("Comment", v.Comment)
                ))
            );
        }

        /// <summary>
        /// Generate POUs XML
        /// </summary>
        private XElement GeneratePousXml(IEnumerable<Pou> pous)
        {
            return new XElement("POUs",
                pous.Select(p => GeneratePouXml(p))
            );
        }

        /// <summary>
        /// Generate single POU XML
        /// </summary>
        private XElement GeneratePouXml(Pou pou)
        {
            var elem = new XElement("POU",
                new XAttribute("Name", pou.Name),
                new XAttribute("Type", pou.Type.ToString()),
                new XAttribute("Language", pou.ImplementationLanguage)
            );

            // Add local variables
            if (pou.LocalTags.Any())
            {
                elem.Add(new XElement("LocalVariables",
                    pou.LocalTags.Select(v => new XElement("Variable",
                        new XAttribute("Name", v.Name),
                        new XAttribute("Type", v.DataType.ToString())
                    ))
                ));
            }

            // Add implementation based on language
            switch (pou.ImplementationLanguage.ToUpper())
            {
                case "LD":
                case "LADDER":
                    elem.Add(GenerateLadderXml(pou.LadderImplementation));
                    break;
                case "SFC":
                    if (pou.SfcImplementation != null)
                        elem.Add(GenerateSfcXml(pou.SfcImplementation));
                    break;
                case "ST":
                    elem.Add(new XElement("StructuredText", pou.StImplementation));
                    break;
                case "FBD":
                default:
                    if (pou.FbdImplementation != null)
                        elem.Add(GenerateFbdXml(pou.FbdImplementation));
                    break;
            }

            return elem;
        }

        /// <summary>
        /// Generate ladder diagram XML
        /// </summary>
        private XElement GenerateLadderXml(IEnumerable<LadderRung> rungs)
        {
            return new XElement("Body",
                new XAttribute("language", "LD"),
                new XElement("NetworkList",
                    rungs.Select((r, i) => new XElement("Network",
                        new XAttribute("Number", i + 1),
                        new XElement("Comment", r.Comment),
                        GenerateRungElementsXml(r)
                    ))
                )
            );
        }

        /// <summary>
        /// Generate rung elements XML
        /// </summary>
        private XElement GenerateRungElementsXml(LadderRung rung)
        {
            var left = new XElement("Left");
            var right = new XElement("Right");

            foreach (var element in rung.Elements)
            {
                if (element is ContactElement contact)
                {
                    left.Add(new XElement("Contact",
                        new XAttribute("Name", contact.Address),
                        new XAttribute("Negated", contact.NormallyClosed.ToString().ToLower()),
                        new XAttribute("Type", contact.ElementType.ToString())
                    ));
                }
            }

            if (rung.Coil != null)
            {
                right.Add(new XElement("Coil",
                    new XAttribute("Name", rung.Coil.Address),
                    new XAttribute("Negated", rung.Coil.Negated.ToString().ToLower()),
                    new XAttribute("SetReset", rung.Coil.SetReset.ToString().ToLower())
                ));
            }

            return new XElement("Elements", left, right);
        }

        /// <summary>
        /// Generate SFC XML
        /// </summary>
        private XElement GenerateSfcXml(SfcChart sfc)
        {
            return new XElement("Body",
                new XAttribute("language", "SFC"),
                new XElement("SFC",
                    new XElement("Steps",
                        sfc.Steps.Select(s => new XElement("Step",
                            new XAttribute("Name", s.Name),
                            new XAttribute("Initial", s.IsInitial.ToString().ToLower()),
                            s.Actions.Any() ? new XElement("Actions",
                                s.Actions.Select(a => new XElement("Action",
                                    new XElement("Name", a.Name),
                                    new XElement("Qualifier", GetQualifierString(a.Qualifier))
                                ))
                            ) : null
                        ))
                    ),
                    new XElement("Transitions",
                        sfc.Transitions.Select(t => new XElement("Transition",
                            new XAttribute("Source", t.SourceStepId),
                            new XAttribute("Target", t.TargetStepId),
                            new XElement("Condition", t.Condition)
                        ))
                    )
                )
            );
        }

        /// <summary>
        /// Generate FBD XML
        /// </summary>
        private XElement GenerateFbdXml(VisualNodeEditorConfig config)
        {
            return new XElement("Body",
                new XAttribute("language", "FBD"),
                new XElement("NetworkList",
                    new XElement("Network",
                        config.Nodes.Select(n => new XElement("Block",
                            new XAttribute("Name", n.Name),
                            new XAttribute("Type", n.ElementType.ToString()),
                            new XAttribute("X", n.X),
                            new XAttribute("Y", n.Y),
                            new XAttribute("Width", n.Width),
                            new XAttribute("Height", n.Height),
                            n.Input1Address?.Address >= 0 ? new XElement("InputAddress", $"{n.Input1Address.Area}:{n.Input1Address.Address}") : null,
                            n.OutputAddress?.Address >= 0 ? new XElement("OutputAddress", $"{n.OutputAddress.Area}:{n.OutputAddress.Address}") : null
                        )),
                        config.Connections.Select(c => new XElement("Connection",
                            new XAttribute("SourceBlock", c.SourceNodeId),
                            new XAttribute("TargetBlock", c.TargetNodeId),
                            new XAttribute("SourceConn", c.SourceConnector),
                            new XAttribute("TargetConn", c.TargetConnector)
                        ))
                    )
                )
            );
        }

        /// <summary>
        /// Generate configurations XML
        /// </summary>
        private XElement GenerateConfigurationsXml(IEnumerable<XefConfiguration> configs)
        {
            return new XElement("Configurations",
                configs.Select(c => new XElement("Configuration",
                    new XAttribute("Name", c.Name),
                    new XElement("Resources",
                        c.Resources.Select(r => new XElement("Resource",
                            new XAttribute("Name", r.Name),
                            r.Tasks.Any() ? new XElement("Tasks",
                                r.Tasks.Select(t => new XElement("Task",
                                    new XAttribute("Name", t.Name),
                                    new XAttribute("Interval", t.IntervalMs),
                                    new XAttribute("Priority", t.Priority),
                                    new XAttribute("Program", t.ProgramName)
                                ))
                            ) : null
                        ))
                    )
                ))
            );
        }

        /// <summary>
        /// Get qualifier string for SFC action
        /// </summary>
        private string GetQualifierString(SfcActionQualifier qualifier)
        {
            return qualifier switch
            {
                SfcActionQualifier.NonStored => "N",
                SfcActionQualifier.Stored => "S",
                SfcActionQualifier.Reset => "R",
                SfcActionQualifier.LeadingEdge => "P",
                SfcActionQualifier.TrailingEdge => "M",
                SfcActionQualifier.Delayed => "D",
                SfcActionQualifier.Limited => "L",
                _ => "N"
            };
        }

        /// <summary>
        /// Convert XEF project to VisualNodeEditorConfig
        /// </summary>
        public async Task<VisualNodeEditorConfig> ConvertXefToVisualNodesAsync(XefProject xefProject)
        {
            var config = new VisualNodeEditorConfig();
            
            // Convert first POU or create default
            var pou = xefProject.Pous.FirstOrDefault() ?? new Pou { Name = "Main" };
            
            if (pou.FbdImplementation != null)
            {
                // Copy nodes and connections
                foreach (var node in pou.FbdImplementation.Nodes)
                    config.Nodes.Add(node);
                foreach (var conn in pou.FbdImplementation.Connections)
                    config.Connections.Add(conn);
            }
            else if (pou.LadderImplementation.Any())
            {
                // Convert ladder to FBD representation
                config = await ConvertLadderToVisualNodesAsync(pou.LadderImplementation);
            }

            return config;
        }

        /// <summary>
        /// Convert ladder diagram to visual nodes
        /// </summary>
        private async Task<VisualNodeEditorConfig> ConvertLadderToVisualNodesAsync(IEnumerable<LadderRung> rungs)
        {
            var config = new VisualNodeEditorConfig();
            double yPos = 100;
            double xPos = 100;

            foreach (var rung in rungs)
            {
                double currentX = xPos;
                VisualNode? lastNode = null;

                // Create nodes for contacts
                foreach (var contact in rung.Elements)
                {
                    if (contact is ContactElement ce)
                    {
                        var node = new VisualNode
                        {
                            Name = $"Contact_{rung.RungNumber}_{ce.Position}",
                            ElementType = ce.NormallyClosed ? PlcElementType.NOT : PlcElementType.Input,
                            X = currentX,
                            Y = yPos,
                            Input1Address = ParseAddressString(ce.Address)
                        };

                        config.Nodes.Add(node);

                        // Connect to previous node
                        if (lastNode != null)
                        {
                            config.Connections.Add(new NodeConnection(lastNode.Id, node.Id));
                        }

                        lastNode = node;
                        currentX += 300;
                    }
                }

                // Create node for coil
                if (rung.Coil != null)
                {
                    var coilNode = new VisualNode
                    {
                        Name = $"Coil_{rung.RungNumber}",
                        ElementType = PlcElementType.Output,
                        X = currentX,
                        Y = yPos,
                        OutputAddress = ParseAddressString(rung.Coil.Address)
                    };

                    config.Nodes.Add(coilNode);

                    // Connect last contact to coil
                    if (lastNode != null)
                    {
                        config.Connections.Add(new NodeConnection(lastNode.Id, coilNode.Id));
                    }
                }

                yPos += 200;
            }

            return config;
        }

        /// <summary>
        /// Convert visual nodes to XEF project
        /// </summary>
        public async Task<XefProject> ConvertVisualNodesToXefAsync(VisualNodeEditorConfig config, string projectName)
        {
            var project = new XefProject
            {
                ProjectInfo = new XefProjectInfo
                {
                    Name = projectName,
                    Vendor = "ModbusForge",
                    Created = DateTime.Now,
                    Modified = DateTime.Now
                }
            };

            // Create main program POU
            var pou = new Pou
            {
                Name = "Main",
                Type = PouType.Program,
                ImplementationLanguage = "FBD",
                FbdImplementation = config
            };

            project.Pous.Add(pou);

            return await Task.FromResult(project);
        }

        /// <summary>
        /// Convert visual nodes to ladder diagram
        /// </summary>
        public async Task<LadderRung[]> ConvertVisualNodesToLadderAsync(VisualNodeEditorConfig config)
        {
            var rungs = new List<LadderRung>();
            
            // Group nodes by Y position (each row becomes a rung)
            var nodeRows = config.Nodes.GroupBy(n => (int)(n.Y / 100) * 100)
                                       .OrderBy(g => g.Key);

            int rungNum = 0;
            foreach (var row in nodeRows)
            {
                var rung = new LadderRung
                {
                    RungNumber = ++rungNum,
                    Comment = $"Generated from FBD row at Y={row.Key}"
                };

                // Sort nodes by X position
                var sortedNodes = row.OrderBy(n => n.X).ToList();

                // Convert input/contact nodes
                foreach (var node in sortedNodes.Where(n => n.ElementType == PlcElementType.Input || 
                                                            n.ElementType == PlcElementType.NOT))
                {
                    rung.AddContact(
                        node.ElementType,
                        node.Input1Address?.DisplayAddress ?? "",
                        node.ElementType == PlcElementType.NOT,
                        rung.Elements.Count
                    );
                }

                // Convert output/coil nodes
                var outputNodes = sortedNodes.Where(n => n.ElementType == PlcElementType.Output).ToList();
                if (outputNodes.Any())
                {
                    var coil = outputNodes.First();
                    rung.SetCoil(new CoilElement
                    {
                        Address = coil.OutputAddress?.DisplayAddress ?? "",
                        Negated = false
                    });
                }

                rungs.Add(rung);
            }

            return await Task.FromResult(rungs.ToArray());
        }

        /// <summary>
        /// Convert visual nodes to SFC chart
        /// </summary>
        public async Task<SfcChart?> ConvertVisualNodesToSfcAsync(VisualNodeEditorConfig config)
        {
            // Check if there are SFC-like structures in the config
            var sfcNodes = config.Nodes.Where(n => 
                n.ElementType == PlcElementType.TON || 
                n.ElementType == PlcElementType.CTU ||
                n.Name.Contains("Step", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (!sfcNodes.Any())
                return null;

            var sfc = new SfcChart { Name = "Auto-generated SFC" };

            // Create steps from sequential nodes
            int stepNum = 0;
            SfcStep? prevStep = null;

            foreach (var node in sfcNodes.OrderBy(n => n.X).ThenBy(n => n.Y))
            {
                var step = new SfcStep
                {
                    Name = node.Name,
                    StepNumber = ++stepNum,
                    IsInitial = stepNum == 1,
                    X = node.X,
                    Y = node.Y
                };

                sfc.Steps.Add(step);

                // Create transition from previous step
                if (prevStep != null)
                {
                    sfc.Transitions.Add(new SfcTransition
                    {
                        SourceStepId = prevStep.Id,
                        TargetStepId = step.Id,
                        Condition = $"{prevStep.Name}_Done"
                    });
                }

                prevStep = step;
            }

            if (sfc.Steps.Any())
                sfc.InitialStepId = sfc.Steps.First().Id;

            return await Task.FromResult(sfc);
        }

        #region Helper Methods

        private static string? GetAttribute(XElement elem, string name)
            => elem.Attribute(name)?.Value;

        private static string GetElementValue(XElement parent, string elementName, string defaultValue = "")
            => parent.Element(elementName)?.Value ?? defaultValue;

        private static bool ParseModbusAddress(string address, out PlcArea area, out int addr)
        {
            area = PlcArea.Coil;
            addr = -1;

            if (string.IsNullOrEmpty(address))
                return false;

            // Parse formats like "HoldingRegister:1", "HR1", "Coil:5", etc.
            var parts = address.Split(':');
            if (parts.Length >= 2)
            {
                if (Enum.TryParse<PlcArea>(parts[0], true, out area) && int.TryParse(parts[1], out addr))
                    return true;
            }
            else if (parts.Length == 1)
            {
                // Try parsing abbreviated format like "HR123"
                var match = System.Text.RegularExpressions.Regex.Match(address, @"^([A-Z]{1,2})(\d+)$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var areaCode = match.Groups[1].Value.ToUpper();
                    area = areaCode switch
                    {
                        "HR" => PlcArea.HoldingRegister,
                        "IR" => PlcArea.InputRegister,
                        "C" or "CO" => PlcArea.Coil,
                        "DI" => PlcArea.DiscreteInput,
                        _ => PlcArea.HoldingRegister
                    };
                    
                    if (int.TryParse(match.Groups[2].Value, out addr))
                        return true;
                }
            }

            return false;
        }

        private static PlcAddressReference ParseAddressString(string address)
        {
            var result = new PlcAddressReference();
            if (!string.IsNullOrEmpty(address) && ParseModbusAddress(address, out var area, out var addr))
            {
                result.Area = area;
                result.Address = addr;
            }
            return result;
        }

        #endregion
    }
}

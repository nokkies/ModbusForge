using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModbusForge.Core.Simulation.Blocks;
using ModbusForge.Core.Simulation.Core;
using ModbusForge.Models;

namespace ModbusForge.Core.Xef
{
    /// <summary>
    /// Result of translating a parsed FBD <see cref="XefProgram"/> into the app's existing
    /// visual-node simulation graph. <see cref="Config"/> is what the existing
    /// <c>VisualSimulationServiceBase</c> executes; the remaining fields tell the UI (and
    /// callers) which blocks were faithfully mapped versus which had to be flagged.
    /// </summary>
    public sealed class XefSimulationGraph
    {
        /// <summary>The emitted visual-node graph (nodes + connections + canvas sizing).</summary>
        public VisualNodeEditorConfig Config { get; init; } = new();

        /// <summary>Blocks that mapped onto a real, executable simulation element type.</summary>
        public IReadOnlyList<XefBlock> MappedBlocks { get; init; } = Array.Empty<XefBlock>();

        /// <summary>
        /// Blocks whose XEF <c>typeName</c> has no simulation equivalent. Each is still emitted
        /// as a visible <see cref="PlcElementType.Unsupported"/> node (flagged), never dropped.
        /// </summary>
        public IReadOnlyList<XefBlock> UnsupportedBlocks { get; init; } = Array.Empty<XefBlock>();

        /// <summary>True when the program is ST (structured text) rather than FBD.</summary>
        public bool IsStProgram { get; init; }

        /// <summary>Human-readable warnings (e.g. unmapped blocks, clamped inputs, unknown constants).</summary>
        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Converts a parsed FBD <see cref="XefProgram"/> (block / pin / link graph) into a
    /// <see cref="VisualNodeEditorConfig"/> that the existing simulation engine can execute.
    ///
    /// <para>
    /// The translation is deliberately conservative:
    /// <list type="bullet">
    ///   <item>One <see cref="VisualNode"/> per <see cref="XefBlock"/>. Its <c>Id</c>/<c>Name</c> is
    ///     the XEF <c>instanceName</c>, its <c>X</c>/<c>Y</c> are <c>PosX</c>/<c>PosY</c> scaled by a
    ///     constant so the XEF canvas fits the editor, and its size is a fixed sensible default.</item>
    ///   <item>The block's <c>typeName</c> maps (case-insensitively) onto a <see cref="PlcElementType"/>.
    ///     Unrecognised types are emitted as <see cref="PlcElementType.Unsupported"/> (visible but inert)
    ///     and reported in <see cref="XefSimulationGraph.UnsupportedBlocks"/> / <see cref="XefSimulationGraph.Notes"/>.</item>
    ///   <item>Each <see cref="XefLink"/> becomes a <see cref="NodeConnection"/>, with the source pin
    ///     mapped to an output connector and the destination pin mapped to the destination block's
    ///     <c>Input1</c>/<c>Input2</c> by its ordinal among that block's declared input ports.</item>
    ///   <item>Pins with an <see cref="XefPin.EffectiveParameter"/> are interpreted as either a
    ///     constant (timer <c>PT</c> → <see cref="VisualNode.TimerPresetMs"/>, comparator →
    ///     <see cref="VisualNode.CompareValue"/>) or a tag/symbol name resolved through the XEF
    ///     variable table into a <see cref="PlcAddressReference"/> bound to the node's input/output
    ///     address slot.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class XefToSimulationConfig
    {
        // Canvas geometry: the XEF stores block positions in a coarser grid than the editor, so
        // scale the raw PosX/PosY by a constant to fit. Node size is fixed and sensible.
        private const double PositionScale = 0.5;
        private const double NodeWidth = 240.0;
        private const double NodeHeight = 140.0;
        private const double CanvasMargin = 40.0;
        private const double MinCanvasSize = 800.0;
        private const int DefaultScanIntervalMs = 100;

        // The visual editor exposes only "Input1"/"Input2" wire connectors; anything beyond the
        // second declared input port has no connector to hang a wire (or address) on.
        private const int MaxEditorInputConnectors = 2;

        // XEF formal-parameter name of the timer preset input (TON/TOF/TP).
        private const string TimerPresetParam = "PT";

        // XEF function-block type names that indicate a real (double) operand for math/comparators.
        private static readonly HashSet<string> RealTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADD_REAL", "SUB_REAL", "MUL_REAL", "DIV_REAL",
            "EQ_REAL", "NE_REAL", "GT_REAL", "LT_REAL", "GE_REAL", "LE_REAL",
            "REAL_EQ", "REAL_NE", "REAL_GT", "REAL_LT", "REAL_GE", "REAL_LE",
            "LIN", "SCALE"
        };

        // XEF typeName -> PlcElementType, keyed on the upper-cased bare name (case-insensitive).
        // Real variants select the _REAL element when the XEF type name indicates a real operand.
        private static readonly Dictionary<string, (PlcElementType Int, PlcElementType? Real)> TypeMap =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["AND"] = (PlcElementType.AND, null),
            ["OR"] = (PlcElementType.OR, null),
            ["NOT"] = (PlcElementType.NOT, null),
            ["XIC"] = (PlcElementType.Input, null),
            ["XIO"] = (PlcElementType.InputBool, null),
            ["XOE"] = (PlcElementType.Output, null),
            ["TON"] = (PlcElementType.TON, null),
            ["TOF"] = (PlcElementType.TOF, null),
            ["TP"] = (PlcElementType.TP, null),
            ["CTU"] = (PlcElementType.CTU, null),
            ["CTD"] = (PlcElementType.CTD, null),
            ["CTC"] = (PlcElementType.CTC, null),
            ["ADD"] = (PlcElementType.MATH_ADD, PlcElementType.MATH_ADD_REAL),
            ["SUB"] = (PlcElementType.MATH_SUB, PlcElementType.MATH_SUB_REAL),
            ["MUL"] = (PlcElementType.MATH_MUL, PlcElementType.MATH_MUL_REAL),
            ["DIV"] = (PlcElementType.MATH_DIV, PlcElementType.MATH_DIV_REAL),
            ["EQ"] = (PlcElementType.COMPARE_EQ, PlcElementType.COMPARE_EQ_REAL),
            ["NE"] = (PlcElementType.COMPARE_NE, PlcElementType.COMPARE_NE_REAL),
            ["GT"] = (PlcElementType.COMPARE_GT, PlcElementType.COMPARE_GT_REAL),
            ["LT"] = (PlcElementType.COMPARE_LT, PlcElementType.COMPARE_LT_REAL),
            ["GE"] = (PlcElementType.COMPARE_GE, PlcElementType.COMPARE_GE_REAL),
            ["LE"] = (PlcElementType.COMPARE_LE, PlcElementType.COMPARE_LE_REAL),
        };

        /// <summary>
        /// Translates a parsed FBD <paramref name="program"/> into a runnable visual-node
        /// simulation graph. For an ST program it returns a graph with zero nodes and
        /// <see cref="XefSimulationGraph.IsStProgram"/> set (no crash).
        /// </summary>
        public static XefSimulationGraph Translate(XefProgram program, XefProject project)
        {
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (string.Equals(program.Language, "ST", StringComparison.OrdinalIgnoreCase))
            {
                return new XefSimulationGraph
                {
                    IsStProgram = true,
                    Config = new VisualNodeEditorConfig
                    {
                        ScanIntervalMs = DefaultScanIntervalMs,
                        ShowLiveValues = true,
                    },
                    Notes = new[] { "Program is written in ST (structured text); no FBD graph was translated." },
                };
            }

            var catalog = BuildCatalog();
            var mapped = new List<XefBlock>();
            var unsupported = new List<XefBlock>();
            var notes = new List<string>();
            var nodesById = new Dictionary<string, VisualNode>(StringComparer.OrdinalIgnoreCase);
            var config = new VisualNodeEditorConfig
            {
                ScanIntervalMs = DefaultScanIntervalMs,
                ShowLiveValues = true,
            };

            foreach (var block in program.Blocks)
            {
                var (elementType, _) = MapBlockType(block.TypeName);
                var node = new VisualNode
                {
                    Id = string.IsNullOrEmpty(block.InstanceName) ? $"{block.TypeName}_{nodesById.Count}" : block.InstanceName,
                    Name = block.InstanceName,
                    ElementType = elementType,
                    X = block.PosX * PositionScale,
                    Y = block.PosY * PositionScale,
                    Width = NodeWidth,
                    Height = NodeHeight,
                    IsEnabled = true,
                };

                config.Nodes.Add(node);
                nodesById[node.Id] = node;

                if (elementType == PlcElementType.Unsupported)
                {
                    unsupported.Add(block);
                    notes.Add($"block {block.InstanceName} type '{block.TypeName}' has no simulation equivalent");
                }
                else
                {
                    mapped.Add(block);
                }

                // Pin-level constants (timer preset, comparator value) are interpreted per node.
                ApplyPinConstants(node, block, elementType, notes);
            }

            // Address bindings from tag/symbol names on pins (and on the block instance name),
            // plus the multi-input limit note. Tag-bound pins on a logic block are realised as
            // helper Input/Output nodes wired into the block (the engine's logic blocks do not
            // carry their own address bindings); blocks that natively take addresses bind directly.
            foreach (var block in program.Blocks)
            {
                if (!nodesById.TryGetValue(block.InstanceName, out var node))
                {
                    continue;
                }

                ApplyPinAddressBindings(node, block, project, catalog, config, nodesById, notes);
            }

            // Wires: one NodeConnection per XefLink.
            foreach (var link in program.Links)
            {
                EmitConnection(link, program, nodesById, catalog, notes, config);
            }

            SizeCanvas(config);

            return new XefSimulationGraph
            {
                Config = config,
                MappedBlocks = mapped,
                UnsupportedBlocks = unsupported,
                IsStProgram = false,
                Notes = notes,
            };
        }

        // ---- function-block catalog (mirrors the one VisualSimulationServiceBase builds) ----

        private static FunctionBlockCatalog BuildCatalog()
        {
            var catalog = new FunctionBlockCatalog();
            catalog.Register(new LegacyInputBlock());
            catalog.Register(new InputBoolBlock());
            catalog.Register(new InputIntBlock());
            catalog.Register(new LegacyOutputBlock());
            catalog.Register(new OutputBoolBlock());
            catalog.Register(new OutputIntBlock());

            catalog.Register(new NotBlock());
            catalog.Register(new AndBlock());
            catalog.Register(new OrBlock());
            catalog.Register(new RsLatchBlock());

            catalog.Register(new TonBlock());
            catalog.Register(new TofBlock());
            catalog.Register(new TpBlock());

            catalog.Register(new CtuBlock());
            catalog.Register(new CtdBlock());
            catalog.Register(new CtcBlock());

            foreach (var operation in Enum.GetValues<ComparisonOperation>())
            {
                catalog.Register(new CompareBlock(operation));
                catalog.Register(new CompareBlock(operation, isReal: true));
            }

            foreach (var operation in Enum.GetValues<MathOperation>())
            {
                catalog.Register(new MathBlock(operation));
                catalog.Register(new MathBlock(operation, isReal: true));
            }

            return catalog;
        }

        // ---- type mapping ----

        private static (PlcElementType Element, string? TypeId) MapBlockType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return (PlcElementType.Unsupported, null);
            }

            var raw = typeName.Trim().ToUpperInvariant();
            bool isReal = RealTypeNames.Contains(raw)
                || raw.EndsWith("_REAL", StringComparison.Ordinal)
                || raw.StartsWith("REAL_", StringComparison.Ordinal);

            // The TypeMap is keyed on the bare name ("EQ"); strip a trailing/leading real marker
            // ("EQ_REAL", "REAL_EQ") so the real variant still hits the same entry, then pick the
            // _REAL element when a real operand was indicated.
            var key = raw;
            if (key.EndsWith("_REAL", StringComparison.Ordinal))
            {
                key = key[..^5];
            }
            else if (key.StartsWith("REAL_", StringComparison.Ordinal))
            {
                key = key[5..];
            }

            if (TypeMap.TryGetValue(key, out var entry))
            {
                var element = isReal && entry.Real != null ? entry.Real.Value : entry.Int;
                return (element, element.ToString());
            }

            return (PlcElementType.Unsupported, null);
        }

        // ---- connections ----

        private static void EmitConnection(
            XefLink link,
            XefProgram program,
            Dictionary<string, VisualNode> nodesById,
            FunctionBlockCatalog catalog,
            List<string> notes,
            VisualNodeEditorConfig config)
        {
            if (!nodesById.TryGetValue(link.SourceBlock, out var sourceNode))
            {
                notes.Add($"link {link.SourceBlock}.{link.SourcePin} -> {link.DestBlock}.{link.DestPin}: source block '{link.SourceBlock}' not in the program");
                return;
            }

            if (!nodesById.TryGetValue(link.DestBlock, out var destNode))
            {
                notes.Add($"link {link.SourceBlock}.{link.SourcePin} -> {link.DestBlock}.{link.DestPin}: destination block '{link.DestBlock}' not in the program");
                return;
            }

            // Source connector: the block's primary output maps to the editor's generic "Output".
            // A named source pin that is not the primary output passes through verbatim.
            var sourceConnector = "Output";
            var sourceBlock = program.FindBlock(link.SourceBlock);
            if (sourceBlock != null)
            {
                var sourcePin = sourceBlock.FindOutput(link.SourcePin);
                if (sourcePin != null)
                {
                    var descriptor = catalog.GetDescriptor(sourceNode.ElementType.ToString());
                    var primary = descriptor != null ? BlockPorts.PrimaryOutput(descriptor.Ports) : null;
                    if (primary != null &&
                        !string.Equals(link.SourcePin, primary, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceConnector = link.SourcePin;
                    }
                }
            }

            // Destination connector: map the XEF input pin to Input1/Input2 by its ordinal among
            // the destination block's declared input ports.
            var destBlock = program.FindBlock(link.DestBlock);
            var destPin = destBlock?.FindInput(link.DestPin);
            if (destPin == null)
            {
                notes.Add($"link {link.SourceBlock}.{link.SourcePin} -> {link.DestBlock}.{link.DestPin}: destination pin '{link.DestPin}' is not an input of '{link.DestBlock}'");
                return;
            }

            var inputOrdinal = IndexOfInputPin(destBlock, destPin);
            if (inputOrdinal >= MaxEditorInputConnectors)
            {
                notes.Add($"link {link.SourceBlock}.{link.SourcePin} -> {link.DestBlock}.{link.DestPin}: '{link.DestBlock}.{link.DestPin}' is input #{inputOrdinal + 1} but the editor exposes only {MaxEditorInputConnectors} input connectors; wire dropped");
                return;
            }

            var targetConnector = inputOrdinal == 1 ? "Input2" : "Input1";

            var connection = new NodeConnection(sourceNode.Id, destNode.Id, targetConnector)
            {
                SourceConnector = sourceConnector,
            };

            config.Connections.Add(connection);
        }

        private static int IndexOfInputPin(XefBlock? block, XefPin pin)
        {
            if (block == null)
            {
                return 0;
            }

            for (var i = 0; i < block.Inputs.Count; i++)
            {
                if (ReferenceEquals(block.Inputs[i], pin))
                {
                    return i;
                }
            }

            // Fall back to a name match (pins are compared by ordinal, but be tolerant).
            var index = block.Inputs.FindIndex(p =>
                string.Equals(p.FormalParameter, pin.FormalParameter, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? 0 : index;
        }

        // ---- pin constants ----

        private static void ApplyPinConstants(VisualNode node, XefBlock block, PlcElementType elementType, List<string> notes)
        {
            // Timer preset: TON/TOF/TP expose a "PT" input that is a TIME constant.
            if (elementType is PlcElementType.TON or PlcElementType.TOF or PlcElementType.TP)
            {
                var pt = block.FindInput(TimerPresetParam);
                if (pt != null && !string.IsNullOrWhiteSpace(pt.EffectiveParameter) && !pt.HasEquation)
                {
                    if (TryParseTimeMs(pt.EffectiveParameter!, out var ms))
                    {
                        node.TimerPresetMs = ms;
                    }
                    else
                    {
                        notes.Add($"block {block.InstanceName}: unsupported timer preset '{pt.EffectiveParameter}'");
                    }
                }
            }

            // Comparator / math second-operand constant (an input with a bare numeric constant).
            if (IsCompareOrMath(elementType))
            {
                ApplySecondOperandConstant(node, block, elementType, notes);
            }
        }

        private static bool IsCompareOrMath(PlcElementType t) => t switch
        {
            PlcElementType.COMPARE_EQ or PlcElementType.COMPARE_NE or PlcElementType.COMPARE_GT
                or PlcElementType.COMPARE_LT or PlcElementType.COMPARE_GE or PlcElementType.COMPARE_LE
                or PlcElementType.COMPARE_EQ_REAL or PlcElementType.COMPARE_NE_REAL or PlcElementType.COMPARE_GT_REAL
                or PlcElementType.COMPARE_LT_REAL or PlcElementType.COMPARE_GE_REAL or PlcElementType.COMPARE_LE_REAL
                or PlcElementType.MATH_ADD or PlcElementType.MATH_SUB or PlcElementType.MATH_MUL or PlcElementType.MATH_DIV
                or PlcElementType.MATH_ADD_REAL or PlcElementType.MATH_SUB_REAL or PlcElementType.MATH_MUL_REAL or PlcElementType.MATH_DIV_REAL
                => true,
            _ => false,
        };

        private static void ApplySecondOperandConstant(VisualNode node, XefBlock block, PlcElementType elementType, List<string> notes)
        {
            bool isReal = elementType.ToString().EndsWith("_REAL");

            // The second declared input (operand B) may carry a bare numeric constant.
            XefPin? second = block.Inputs.Count >= 2 ? block.Inputs[1] : null;
            if (second == null || string.IsNullOrWhiteSpace(second.EffectiveParameter) || second.HasEquation)
            {
                return;
            }

            if (!double.TryParse(second.EffectiveParameter, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                // A non-numeric constant on an operand input is likely a tag name (handled by the
                // address pass) or genuinely unsupported. Only note when it is not a tag-like name.
                if (!LooksLikeTag(second.EffectiveParameter))
                {
                    notes.Add($"block {block.InstanceName}: unsupported operand constant '{second.EffectiveParameter}'");
                }

                return;
            }

            if (isReal)
            {
                node.CompareValueReal = value;
            }
            else if (IsCompare(elementType))
            {
                node.CompareValue = (int)Math.Round(value);
            }
            else
            {
                // Math blocks reuse CompareValue/CompareValueReal as their "Constant" parameter.
                node.CompareValue = (int)Math.Round(value);
                if (isReal)
                {
                    node.CompareValueReal = value;
                }
            }
        }

        private static bool IsCompare(PlcElementType t) => t switch
        {
            PlcElementType.COMPARE_EQ or PlcElementType.COMPARE_NE or PlcElementType.COMPARE_GT
                or PlcElementType.COMPARE_LT or PlcElementType.COMPARE_GE or PlcElementType.COMPARE_LE
                or PlcElementType.COMPARE_EQ_REAL or PlcElementType.COMPARE_NE_REAL or PlcElementType.COMPARE_GT_REAL
                or PlcElementType.COMPARE_LT_REAL or PlcElementType.COMPARE_GE_REAL or PlcElementType.COMPARE_LE_REAL
                => true,
            _ => false,
        };

        // ---- address bindings from tag/symbol names ----

        private static void ApplyPinAddressBindings(
            VisualNode node, XefBlock block, XefProject project, FunctionBlockCatalog catalog,
            VisualNodeEditorConfig config, Dictionary<string, VisualNode> nodesById, List<string> notes)
        {
            if (node.ElementType == PlcElementType.Unsupported)
            {
                return;
            }

            var descriptor = catalog.GetDescriptor(node.ElementType.ToString());
            if (descriptor == null)
            {
                return;
            }

            var inputPorts = BlockPorts.Inputs(descriptor.Ports);
            var primaryOutput = BlockPorts.PrimaryOutput(descriptor.Ports);
            // Do this block type natively read/write an address on its ports, or must the tag be
            // realised as a helper I/O node wired into it? Logic blocks (AND/OR/NOT/timers/…) do
            // not carry their own address bindings, so their tags become helper nodes + wires.
            var nodeDescriptor = NodeDescriptors.TryGet(node.ElementType, out var nd) ? nd : null;
            var nativeInputBinding = nodeDescriptor is { HasInput1Address: true }
                             || nodeDescriptor is { HasInput2Address: true };
            var nativeOutputBinding = nodeDescriptor is { HasOutputAddress: true };

            // Input pins: a tag/symbol name with a resolvable topological address.
            var xefInputs = block.Inputs;
            for (var i = 0; i < xefInputs.Count; i++)
            {
                var pin = xefInputs[i];
                if (string.IsNullOrWhiteSpace(pin.EffectiveParameter) || pin.HasEquation)
                {
                    continue;
                }

                // Bind only tag/symbol names; numeric constants and T# time literals are handled
                // by the constant pass (and have no address to bind to).
                if (!LooksLikeTag(pin.EffectiveParameter))
                {
                    continue;
                }

                if (!TryResolveAddress(project, pin.EffectiveParameter!, out var address))
                {
                    continue;
                }

                if (i >= MaxEditorInputConnectors || i >= inputPorts.Count)
                {
                    notes.Add($"block {block.InstanceName}: input #{i + 1} has no address connector; binding to tag '{pin.EffectiveParameter}' not applied");
                    continue;
                }

                if (nativeInputBinding)
                {
                    // The block natively reads an address on this port (e.g. an Input node).
                    SetAddress(node, i, address);
                }
                else
                {
                    // Logic block: emit a helper Input node bound to the tag and wire it to the
                    // block's i-th input port (the engine reads it from the wire). The connector
                    // uses the generic editor names so MapInputPort resolves it positionally.
                    var helper = EmitInputHelper(node, i, address, pin.EffectiveParameter!, config, nodesById);
                    if (helper != null)
                    {
                        config.Connections.Add(new NodeConnection(helper.Id, node.Id, i == 1 ? "Input2" : "Input1"));
                    }
                }
            }

            // A bound output: the primary output pin's tag name, or the block instance name itself.
            var outputTag = block.Outputs.FirstOrDefault(o =>
                !string.IsNullOrWhiteSpace(o.EffectiveParameter) && !o.HasEquation && LooksLikeTag(o.EffectiveParameter))?.EffectiveParameter;

            if (outputTag == null && project.FindVariable(block.InstanceName)?.TryParseAddress() is { })
            {
                outputTag = block.InstanceName;
            }

            if (string.IsNullOrEmpty(outputTag) || primaryOutput == null)
            {
                return;
            }

            if (!TryResolveAddress(project, outputTag, out var outputAddress))
            {
                return;
            }

            if (nativeOutputBinding)
            {
                // The block natively writes an address on its output.
                node.OutputAddress = new PlcAddressReference
                {
                    Area = outputAddress.Area,
                    Address = outputAddress.Address,
                    Not = false,
                    SymbolicName = outputTag,
                };
            }
            else
            {
                // Logic block: emit a helper Output node bound to the tag and wire the block's
                // primary output into it.
                var helper = EmitOutputHelper(node, outputTag, outputAddress, config, nodesById);
                if (helper != null)
                {
                    config.Connections.Add(new NodeConnection(node.Id, helper.Id, "Input1"));
                }
            }
        }

        /// <summary>
        /// Creates (and caches) an Input helper node that reads <paramref name="address"/> and
        /// feeds <paramref name="hostNode"/>. Returns the helper, or null if it could not be made.
        /// </summary>
        private static VisualNode? EmitInputHelper(
            VisualNode hostNode, int inputOrdinal, (PlcArea Area, int Address) address,
            string tag, VisualNodeEditorConfig config, Dictionary<string, VisualNode> nodesById)
        {
            var isReal = address.Area == PlcArea.HoldingRegister || address.Area == PlcArea.InputRegister;
            var id = $"{hostNode.Id}__in{inputOrdinal}_{tag}";
            if (nodesById.TryGetValue(id, out var existing))
            {
                return existing;
            }

            var helper = new VisualNode
            {
                Id = id,
                Name = tag,
                ElementType = isReal ? PlcElementType.InputInt : PlcElementType.InputBool,
                X = hostNode.X - 120,
                Y = hostNode.Y + inputOrdinal * 30,
                Width = NodeWidth,
                Height = NodeHeight,
                IsEnabled = true,
                Input1Address = new PlcAddressReference
                {
                    Area = address.Area,
                    Address = address.Address,
                    Not = false,
                    SymbolicName = tag,
                },
            };

            config.Nodes.Add(helper);
            nodesById[id] = helper;
            return helper;
        }

        /// <summary>
        /// Creates (and caches) an Output helper node that <paramref name="hostNode"/> feeds and
        /// that writes <paramref name="address"/>. Returns the helper, or null if it could not be made.
        /// </summary>
        private static VisualNode? EmitOutputHelper(
            VisualNode hostNode, string tag, (PlcArea Area, int Address) address,
            VisualNodeEditorConfig config, Dictionary<string, VisualNode> nodesById)
        {
            var id = $"{hostNode.Id}__out_{tag}";
            if (nodesById.TryGetValue(id, out var existing))
            {
                return existing;
            }

            var isReal = address.Area == PlcArea.HoldingRegister || address.Area == PlcArea.InputRegister;
            var helper = new VisualNode
            {
                Id = id,
                Name = tag,
                ElementType = isReal ? PlcElementType.OutputInt : PlcElementType.OutputBool,
                X = hostNode.X + NodeWidth + 120,
                Y = hostNode.Y,
                Width = NodeWidth,
                Height = NodeHeight,
                IsEnabled = true,
                OutputAddress = new PlcAddressReference
                {
                    Area = address.Area,
                    Address = address.Address,
                    Not = false,
                    SymbolicName = tag,
                },
            };

            config.Nodes.Add(helper);
            nodesById[id] = helper;
            return helper;
        }

        private static void SetAddress(VisualNode node, int inputOrdinal, (PlcArea Area, int Address) address)
        {
            if (inputOrdinal == 1)
            {
                node.Input2Address = new PlcAddressReference
                {
                    Area = address.Area,
                    Address = address.Address,
                    Not = false,
                };
            }
            else
            {
                node.Input1Address = new PlcAddressReference
                {
                    Area = address.Area,
                    Address = address.Address,
                    Not = false,
                };
            }
        }

        private static bool TryResolveAddress(XefProject project, string tag, out (PlcArea Area, int Address) address)
        {
            address = default;
            var variable = project.FindVariable(tag);
            if (variable == null)
            {
                return false;
            }

            if (variable.TryParseAddress() is not { } parsed)
            {
                return false;
            }

            address = parsed;
            return true;
        }

        /// <summary>
        /// Heuristic: a pin effective parameter is treated as a tag/symbol name when it is not a
        /// plain number and not a T# time literal. Dotted member access ("Motor.Run") is still a tag.
        /// </summary>
        private static bool LooksLikeTag(string value)
        {
            var token = value.Trim();
            if (token.Length == 0)
            {
                return false;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            if (token.StartsWith("T#", StringComparison.OrdinalIgnoreCase) &&
                TryParseTimeMs(token, out _))
            {
                return false;
            }

            return true;
        }

        // ---- T# time parsing (T#5s -> 5000, T#500ms -> 500, T#0.5s -> 500, plain ms -> as-is) ----

        private static bool TryParseTimeMs(string text, out int milliseconds)
        {
            milliseconds = 0;
            var token = text.Trim();
            if (token.StartsWith("T#", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring(2).Trim();
            }

            if (token.Length == 0)
            {
                return false;
            }

            var multiplier = 1.0;
            // Units are suffixes on the numeric part: "ms" (2 chars) or a single char
            // (s/d/h/m). "ms" must be checked first because it ends in "s".
            if (token.Length >= 3 && string.Equals(token[^2..], "ms", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1.0;
                token = token[..^2].TrimEnd();
            }
            else if (token.Length >= 2)
            {
                switch (token[^1].ToString().ToLowerInvariant())
                {
                    case "s": multiplier = 1000.0; token = token[..^1].TrimEnd(); break;
                    case "d": multiplier = 86400000.0; token = token[..^1].TrimEnd(); break;
                    case "h": multiplier = 3600000.0; token = token[..^1].TrimEnd(); break;
                    case "m": multiplier = 60000.0; token = token[..^1].TrimEnd(); break;
                    // Any other trailing character: leave the token intact so the final
                    // numeric parse rejects it (the value is treated as unsupported).
                }
            }

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (number < 0)
            {
                return false;
            }

            milliseconds = (int)Math.Round(number * multiplier);
            return true;
        }

        // ---- canvas sizing ----

        private static void SizeCanvas(VisualNodeEditorConfig config)
        {
            if (config.Nodes.Count == 0)
            {
                config.CanvasWidth = MinCanvasSize;
                config.CanvasHeight = MinCanvasSize;
                return;
            }

            var maxX = config.Nodes.Max(n => n.X + n.Width);
            var maxY = config.Nodes.Max(n => n.Y + n.Height);

            config.CanvasWidth = Math.Max(MinCanvasSize, maxX + CanvasMargin);
            config.CanvasHeight = Math.Max(MinCanvasSize, maxY + CanvasMargin);
        }
    }
}

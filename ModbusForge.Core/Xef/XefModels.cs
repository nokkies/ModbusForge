using System;
using System.Collections.Generic;

namespace ModbusForge.Core.Xef
{
    /// <summary>
    /// A parsed Schneider Unity/ControlExpert .XEF project: the controller, its task/section
    /// table, the variable (IO) table, user data types, function-block type definitions and the
    /// programs. The XEF stores program logic as an FBD block/pin/link graph (not as text); the
    /// model here mirrors that graph.
    ///
    /// Element names are the literal XEF XML grammar confirmed by a working parser
    /// (nokkies/UnityParseEngine, builder/unity.py). See docs/research/xef-plc-simulation-integration.md.
    /// </summary>
    public sealed class XefProject
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>The source file path this project was loaded from (for display).</summary>
        public string? SourcePath { get; set; }

        /// <summary>Task/section table keyed by section name, in execution order.</summary>
        public List<XefTask> Tasks { get; } = new();

        /// <summary>The variable / IO table (dataBlock/variables), keyed by lower-case name.</summary>
        public List<XefVariable> Variables { get; } = new();

        /// <summary>User-defined data types (DDTSource), keyed by lower-case name.</summary>
        public List<XefDataType> DataTypes { get; } = new();

        /// <summary>True when any program in the file was written in ST (not FBD).</summary>
        public bool HasStPrograms { get; set; }

        /// <summary>True when any program in the file was written in Ladder Diagram (LD).</summary>
        public bool HasLadderPrograms { get; set; }

        /// <summary>Function-block / function type signatures (FBSource/EFSource/EFBSource).</summary>
        public List<XefBlockType> BlockTypes { get; } = new();

        /// <summary>Programs, keyed by lower-case name, in file order.</summary>
        public List<XefProgram> Programs { get; } = new();

        /// <summary>Any non-fatal warnings collected while parsing (e.g. unknown FB types).</summary>
        public List<string> Warnings { get; } = new();

        public XefVariable? FindVariable(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var key = name.ToLowerInvariant();
            return Variables.Find(v => string.Equals(v.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        public XefProgram? FindProgram(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            return Programs.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>A task / section (sectionDesc) with its execution ordering.</summary>
    public sealed class XefTask
    {
        public string Name { get; init; } = string.Empty;
        public string FMName { get; init; } = string.Empty;
        public string FMId { get; init; } = string.Empty;
        public string FMOrder { get; init; } = string.Empty;
        public int SectionOrder { get; init; }
    }

    /// <summary>
    /// A variable (dataBlock/variables). <see cref="Address"/> is the XEF
    /// <c>topologicalAddress</c> — the IO address the PLC maps this symbol to.
    /// </summary>
    public sealed class XefVariable
    {
        public string Name { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string? Address { get; init; }
        public string? Value { get; init; }
        public string? Comment { get; init; }

        /// <summary>UDT member initial values (instanceElementDesc), if this variable is a struct.</summary>
        public Dictionary<string, string> Members { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Best-effort parse of <see cref="Address"/> into a Modbus area + 1-based display
        /// address, or null when the address is empty / not a simple integer IO reference.
        /// </summary>
        public (Models.PlcArea Area, int Address)? TryParseAddress()
        {
            if (string.IsNullOrWhiteSpace(Address))
            {
                return null;
            }

            var token = Address.Trim();
            if (!int.TryParse(token, out var addr) || addr <= 0)
            {
                return null;
            }

            // The data type is the authoritative hint for which Modbus area the IO lives in:
            // a BOOL topological address is a coil, a real/integer one is a holding register.
            // (The topological address is still the authoritative *number*; the UI can re-map.)
            var (area, _, _) = XefTypeMapper.MapDataType(DataType);
            return (area, addr);
        }
    }

    /// <summary>A user-defined data type (DDTSource) with its struct members.</summary>
    public sealed class XefDataType
    {
        public string Name { get; init; } = string.Empty;
        public List<XefDataTypeMember> Members { get; } = new();
    }

    public sealed class XefDataTypeMember
    {
        public string Name { get; init; } = string.Empty;
        public string DataType { get; init; } = string.Empty;
        public string? Comment { get; init; }
    }

    /// <summary>A function-block / function type signature (FBSource/EFSource/EFBSource).</summary>
    public sealed class XefBlockType
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Which source element it came from: "FB", "EF" or "EFB".</summary>
        public string SourceKind { get; init; } = "FB";

        public List<XefBlockTypePin> Inputs { get; } = new();
        public List<XefBlockTypePin> Outputs { get; } = new();
        public List<XefBlockTypePin> InOuts { get; } = new();
    }

    public sealed class XefBlockTypePin
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Direction { get; init; } = "i"; // i | o | io
    }

    /// <summary>A program (program element) — either FBD (block graph) or ST (text).</summary>
    public sealed class XefProgram
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>"FBD" when the program is a block graph, "ST" when it is structured text.</summary>
        public string Language { get; set; } = "FBD";

        /// <summary>The FBD blocks in this program (empty for ST programs).</summary>
        public List<XefBlock> Blocks { get; } = new();

        /// <summary>The FBD links (wires) between block pins.</summary>
        public List<XefLink> Links { get; } = new();

        /// <summary>Comments (textBox) in this program.</summary>
        public List<XefComment> Comments { get; } = new();

        /// <summary>The raw ST source text, when this program is ST-written (otherwise null).</summary>
        public string? StSource { get; set; }

        public XefBlock? FindBlock(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName))
            {
                return null;
            }

            return Blocks.Find(b => string.Equals(b.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when this program is a Ladder Diagram (LD) network list.</summary>
        public bool IsLadder => Language == "LD" && Rungs.Count > 0;

        /// <summary>The ladder rungs (networkLAD), in order. Empty for FBD/ST programs.</summary>
        public List<XefLadderRung> Rungs { get; } = new();
    }

    /// <summary>
    /// The kind of a single element on a ladder rung (IEC 61131-3 LD symbols).
    /// </summary>
    public enum LadderElementKind
    {
        /// <summary>Normally-open contact (two bars, gap) — energises when the tag is TRUE.</summary>
        Contact,

        /// <summary>Normally-closed contact (two bars + diagonal slash) — energises when the tag is FALSE.</summary>
        ContactNegated,

        /// <summary>Rising-edge contact (NO + edge marker) — true only on the 0→1 transition.</summary>
        ContactRise,

        /// <summary>Falling-edge contact (NO + edge marker) — true only on the 1→0 transition.</summary>
        ContactFall,

        /// <summary>Output coil (circle/parentheses) — sets the tag to the rung power state.</summary>
        Coil,

        /// <summary>Negated coil (circle + slash) — sets the tag to the inverted rung state.</summary>
        CoilNegated,

        /// <summary>Set coil (circle + S) — latches the tag TRUE.</summary>
        CoilSet,

        /// <summary>Reset coil (circle + R) — latches the tag FALSE.</summary>
        CoilReset,
    }

    /// <summary>
    /// A single element on a ladder rung: a contact, coil, set/reset coil, or an inline
    /// function block. <see cref="Col"/> is the element's column within the rung (left→right);
    /// <see cref="Row"/> is the branch offset for parallel branches (0 = main rung line).
    /// </summary>
    public sealed class XefLadderElement
    {
        public LadderElementKind Kind { get; init; }

        /// <summary>The tag / symbol / bit address the element acts on (or an instance name for a block).</summary>
        public string Tag { get; init; } = string.Empty;

        /// <summary>The function-block type name when this element is an inline FB (else empty).</summary>
        public string BlockType { get; init; } = string.Empty;

        /// <summary>Column position within the rung (0-based, left→right).</summary>
        public int Col { get; init; }

        /// <summary>Row offset within the rung (0 = main rung; &gt;0 = parallel branch below).</summary>
        public int Row { get; init; }

        /// <summary>True when the element is an inline function block (renders as an FBD box).</summary>
        public bool IsBlock => !string.IsNullOrEmpty(BlockType);
    }

    /// <summary>A ladder rung (networkLAD): a power rail + a sequence of elements.</summary>
    public sealed class XefLadderRung
    {
        /// <summary>Zero-based rung index (the "network number" is index + 1).</summary>
        public int Index { get; init; }

        /// <summary>The elements on this rung, in left→right (and branch) order.</summary>
        public List<XefLadderElement> Elements { get; } = new();
    }

    /// <summary>A function-block instance (FFBBlock) with its pins and canvas position.</summary>
    public sealed class XefBlock
    {
        public string InstanceName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public bool EnEno { get; init; }

        /// <summary>Canvas position in XEF grid cells (not pixels).</summary>
        public int PosX { get; init; }

        /// <summary>Canvas position in XEF grid cells (not pixels).</summary>
        public int PosY { get; init; }

        /// <summary>Block width in XEF grid cells (the FFBBlock <c>width</c> attribute).</summary>
        public int Width { get; init; }

        /// <summary>Block height in XEF grid cells (the FFBBlock <c>height</c> attribute).</summary>
        public int Height { get; init; }

        public List<XefPin> Inputs { get; } = new();
        public List<XefPin> Outputs { get; } = new();

        public XefPin? FindInput(string formalParameter)
            => Inputs.Find(p => string.Equals(p.FormalParameter, formalParameter, StringComparison.OrdinalIgnoreCase));

        public XefPin? FindOutput(string formalParameter)
            => Outputs.Find(p => string.Equals(p.FormalParameter, formalParameter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A pin on a block (inputVariable / outputVariable).</summary>
    public sealed class XefPin
    {
        public string FormalParameter { get; init; } = string.Empty;

        /// <summary>
        /// The effective value: a constant ("5", "2.5"), a tag/symbol name ("Motor.Run"),
        /// or an inline equation. Null when the pin is only wired via a link.
        /// </summary>
        public string? EffectiveParameter { get; init; }
        public bool Inverted { get; init; }

        /// <summary>True when <see cref="EffectiveParameter"/> looks like an inline equation.</summary>
        public bool HasEquation { get; init; }
    }

    /// <summary>A wire (linkFB) between a source block's pin and a destination block's pin.</summary>
    public sealed class XefLink
    {
        public string SourceBlock { get; init; } = string.Empty;
        public string SourcePin { get; init; } = string.Empty;
        public string DestBlock { get; init; } = string.Empty;
        public string DestPin { get; init; } = string.Empty;
    }

    /// <summary>A free-floating comment (textBox) on the program canvas.</summary>
    public sealed class XefComment
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string Text { get; init; } = string.Empty;
    }
}

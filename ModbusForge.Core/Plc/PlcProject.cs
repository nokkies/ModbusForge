using System;
using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>A declared variable (the XEF dataBlock), located or not.</summary>
    public sealed class PlcVariable
    {
        public PlcVariable(string name, PlcType type, PlcAddress? address)
        {
            Name = name;
            Type = type;
            Address = address;
        }

        public string Name { get; }
        public PlcType Type { get; }

        /// <summary>The topological address it is located on; null for unlocated variables.</summary>
        public PlcAddress? Address { get; }

        /// <summary>Declared initial values: path inside the variable ("" for the whole) and literal.</summary>
        public List<PlcInitialValue> InitialValues { get; } = new();

        public override string ToString() => Name;
    }

    /// <summary>One declared initial value: <c>Path</c> is "" (the variable), "PT", "[3]", "a.b", ...</summary>
    public sealed record PlcInitialValue(string Path, string Text);

    /// <summary>How a block computes.</summary>
    public enum PlcBlockKind
    {
        /// <summary>A standard function (EF) the runtime executes.</summary>
        Function,

        /// <summary>A standard function block (EFB) the runtime executes.</summary>
        FunctionBlock,

        /// <summary>A user function block (DFB): its code is not executed (most exports encrypt it).</summary>
        UserFunctionBlock,

        /// <summary>A standard type the runtime does not simulate.</summary>
        Unsupported
    }

    /// <summary>A pin of a block: its formal name and what feeds it (a link or an actual parameter).</summary>
    public sealed class PlcBlockPin
    {
        public PlcBlockPin(string name, bool inverted, PlcOperand? operand)
        {
            Name = name;
            Inverted = inverted;
            Operand = operand;
        }

        public string Name { get; }

        /// <summary>The pin is negated (drawn with a circle).</summary>
        public bool Inverted { get; }

        /// <summary>The actual parameter: read for inputs, written for outputs.</summary>
        public PlcOperand? Operand { get; }

        /// <summary>For an input: the block whose output feeds it through a link.</summary>
        public PlcBlock? SourceBlock { get; internal set; }

        /// <summary>Index into <see cref="PlcBlock.Outputs"/> of the source, or -1 for its ENO.</summary>
        public int SourceOutput { get; internal set; } = -1;

        public bool IsLinked => SourceBlock != null;

        public bool IsConnected => SourceBlock != null || Operand != null;

        /// <summary>The actual parameter is a whole structure, array or undefined type.</summary>
        public bool CarriesStructure => Operand?.StaticType is { } type && (type.IsStructured || type.Kind == PlcTypeKind.Unknown);
    }

    /// <summary>An FFB instance in a section, with its compiled pins and runtime state.</summary>
    public sealed class PlcBlock
    {
        public PlcBlock(string typeName, string instanceName, PlcBlockKind kind, PlcBlockBehavior? behavior,
            IReadOnlyList<PlcBlockPin> inputs, IReadOnlyList<PlcBlockPin> outputs, PlcBlockPin? en, PlcBlockPin? eno)
        {
            TypeName = typeName;
            InstanceName = instanceName;
            Kind = kind;
            Behavior = behavior;
            Inputs = inputs;
            Outputs = outputs;
            En = en;
            Eno = eno;
            Call = new PlcCall(inputs.Select(p => p.Name).ToArray(), outputs.Select(p => p.Name).ToArray());
            OutputLinks = new PlcValue[outputs.Count];
            LastInputs = new PlcValue[inputs.Count];
        }

        public string TypeName { get; }

        /// <summary>The instance name from the XEF (".4" for functions, "FBI_12" for function blocks).</summary>
        public string InstanceName { get; }

        /// <summary>The editor node this block is drawn as, when imported through the PLC tab.</summary>
        public string? NodeId { get; internal set; }

        public PlcBlockKind Kind { get; }

        /// <summary>Null for blocks that do not compute (user function blocks, unsupported types).</summary>
        public PlcBlockBehavior? Behavior { get; }

        public bool IsSimulated => Behavior != null;

        public bool IsFunctionBlock => Behavior?.IsFunctionBlock ?? Kind != PlcBlockKind.Function;

        /// <summary>Data inputs in pin order (EN excluded).</summary>
        public IReadOnlyList<PlcBlockPin> Inputs { get; }

        /// <summary>Data outputs in pin order (ENO excluded).</summary>
        public IReadOnlyList<PlcBlockPin> Outputs { get; }

        public PlcBlockPin? En { get; }
        public PlcBlockPin? Eno { get; }

        /// <summary>The declared instance (function blocks): its fields hold the parameters.</summary>
        public PlcVariable? Instance { get; internal set; }

        /// <summary>Grid cell of the block's top-left corner (execution order ties).</summary>
        public double X { get; internal set; }
        public double Y { get; internal set; }

        /// <summary>The instance this block is forced to run after ("Execute after"), if any.</summary>
        public string? ExecuteAfter { get; internal set; }

        // ---- runtime state ------------------------------------------------

        internal PlcCall Call { get; }
        internal object? State { get; set; }

        /// <summary>Instance fields per pin, resolved on the first execution.</summary>
        internal PinFields? InstanceFields { get; set; }

        internal sealed record PinFields(PlcLocation?[] Inputs, PlcLocation?[] Outputs);

        /// <summary>Values on the output links after the last execution (pin negation applied).</summary>
        public PlcValue[] OutputLinks { get; }

        /// <summary>Values read at the input pins in the last scan (before pin negation).</summary>
        public PlcValue[] LastInputs { get; }

        /// <summary>ENO after the last scan; also the value on a link from the ENO pin.</summary>
        public bool LastEno { get; internal set; }

        /// <summary>The value EN had in the last scan (before pin negation); null when EN is free.</summary>
        public bool? LastEn { get; internal set; }

        /// <summary>True when the block ran in the last scan (EN was 1 and its section was active).</summary>
        public bool ExecutedLastScan { get; internal set; }

        public override string ToString() => $"{InstanceName} [{TypeName}]";
    }

    /// <summary>An FBD section in its task, with its blocks in execution order.</summary>
    public sealed class PlcSection
    {
        public PlcSection(string name, string task, IReadOnlyList<PlcBlock> blocks, PlcOperand? condition)
        {
            Name = name;
            Task = task;
            Blocks = blocks;
            Condition = condition;
        }

        public string Name { get; }
        public string Task { get; }

        /// <summary>Blocks in execution order.</summary>
        public IReadOnlyList<PlcBlock> Blocks { get; }

        /// <summary>The section's activation condition (a BOOL variable), if any.</summary>
        public PlcOperand? Condition { get; }

        /// <summary>False when the activation condition held the section off in the last scan.</summary>
        public bool ActiveLastScan { get; internal set; }
    }

    /// <summary>A task (MAST, FAST) and its FBD sections in declaration order.</summary>
    public sealed class PlcTask
    {
        public PlcTask(string name, string kind, IReadOnlyList<PlcSection> sections)
        {
            Name = name;
            Kind = kind;
            Sections = sections;
        }

        public string Name { get; }
        public string Kind { get; }
        public IReadOnlyList<PlcSection> Sections { get; }

        internal bool FirstCycle { get; set; } = true;
    }

    /// <summary>The executable model of an imported Unity project.</summary>
    public sealed class PlcProject
    {
        public PlcProject(IReadOnlyDictionary<string, PlcVariable> variables, IReadOnlyList<PlcTask> tasks)
        {
            Variables = variables;
            Tasks = tasks;
        }

        /// <summary>Declared variables by name (case-insensitive, as Unity identifiers are).</summary>
        public IReadOnlyDictionary<string, PlcVariable> Variables { get; }

        /// <summary>Tasks in scan order: FAST before MAST.</summary>
        public IReadOnlyList<PlcTask> Tasks { get; }

        public IEnumerable<PlcSection> Sections => Tasks.SelectMany(t => t.Sections);

        public IEnumerable<PlcBlock> Blocks => Sections.SelectMany(s => s.Blocks);

        /// <summary>Sections in other languages (ST, LD, IL) the runtime does not execute.</summary>
        public List<string> SkippedSections { get; } = new();

        public List<string> Warnings { get; } = new();
    }
}

using System;
using System.Collections.Generic;
using ModbusForge.Data;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Executes an imported Unity project the way the controller's tasks do: every
    /// scan runs each task's FBD sections in order, and each section's blocks in their
    /// execution order. Located variables live in the Modbus data store (the Quantum
    /// state RAM), so a scan reads and writes the registers other programs see;
    /// unlocated variables live in the runtime's own memory.
    /// </summary>
    /// <remarks>
    /// Blocks keep their scan state (timers, edges, link values) on the project model,
    /// so a project is run by one runtime at a time; compile it again for another.
    /// </remarks>
    public sealed class PlcRuntime
    {
        // %S bits the runtime drives (Unity Pro system objects: %S0 cold start,
        // %S13 first cycle in RUN, %S21 first task cycle, %S4-%S7 time bases).
        private const int ColdStartBit = 0;
        private const int TimeBase10MsBit = 4;
        private const int TimeBase100MsBit = 5;
        private const int TimeBase1SBit = 6;
        private const int TimeBase1MinBit = 7;
        private const int FirstCycleInRunBit = 13;
        private const int FirstTaskCycleBit = 21;

        private readonly Dictionary<PlcVariable, PlcByteSpace> _storage = new();
        private double _clock;
        private bool _coldStart = true;
        private DataStore? _initializedStore;

        public PlcRuntime(PlcProject project)
        {
            Project = project ?? throw new ArgumentNullException(nameof(project));
            Memory = new PlcMemory();
            InitializeVariables(located: false);
        }

        public PlcProject Project { get; }

        public PlcMemory Memory { get; }

        /// <summary>Controller time in milliseconds (sum of the scan periods run so far).</summary>
        public long Now => (long)_clock;

        public long ScanCount { get; private set; }

        /// <summary>Array accesses outside the declared bounds since the runtime started.</summary>
        public int IndexErrors { get; private set; }

        /// <summary>Blocks whose execution threw; they behave as if ENO were 0.</summary>
        public int BlockErrors { get; private set; }

        /// <summary>
        /// Marks the next scan as the first cycle of every task (%S21 set), as a
        /// controller does when it goes to RUN.
        /// </summary>
        public void Start()
        {
            foreach (var task in Project.Tasks) task.FirstCycle = true;
        }

        /// <summary>
        /// Runs one scan against <paramref name="store"/>. The caller holds the store's
        /// lock for the duration (as the simulation engine does).
        /// </summary>
        public void Scan(DataStore? store, TimeSpan elapsed)
        {
            Memory.Store = store;

            // Located variables get their declared initial values in each data store the
            // runtime starts scanning (the offline store, then the Modbus server's once
            // it is started), as a cold start gives them in the controller.
            if (store != null && !ReferenceEquals(store, _initializedStore))
            {
                InitializeVariables(located: true);
                _initializedStore = store;
            }

            _clock += Math.Max(0, elapsed.TotalMilliseconds);
            var bits = Memory.SystemBits;
            bits.WriteBit(ColdStartBit, _coldStart);
            bits.WriteBit(FirstCycleInRunBit, _coldStart);
            bits.WriteBit(TimeBase10MsBit, Now / 5 % 2 == 1);
            bits.WriteBit(TimeBase100MsBit, Now / 50 % 2 == 1);
            bits.WriteBit(TimeBase1SBit, Now / 500 % 2 == 1);
            bits.WriteBit(TimeBase1MinBit, Now / 30_000 % 2 == 1);

            foreach (var task in Project.Tasks)
            {
                bits.WriteBit(FirstTaskCycleBit, task.FirstCycle || _coldStart);
                foreach (var section in task.Sections)
                {
                    RunSection(section, task);
                }
                task.FirstCycle = false;
            }

            bits.WriteBit(FirstTaskCycleBit, false);
            bits.WriteBit(ColdStartBit, false);
            bits.WriteBit(FirstCycleInRunBit, false);
            _coldStart = false;
            ScanCount++;
        }

        /// <summary>Reads a variable, field or direct address by its Unity name ("Pump.Run", "%MW10").</summary>
        public PlcValue Read(string reference) => PlcOperandParser.Parse(reference, Project.Variables).Read(this);

        /// <summary>Writes a variable, field or direct address by its Unity name.</summary>
        public void Write(string reference, PlcValue value) => PlcOperandParser.Parse(reference, Project.Variables).Write(this, value);

        /// <summary>The memory a variable occupies: its located address, or its private storage.</summary>
        public PlcLocation Locate(PlcVariable variable)
        {
            if (variable.Address != null)
            {
                return Memory.Locate(variable.Address, variable.Address.Bit.HasValue ? PlcType.Bool : variable.Type);
            }

            if (!_storage.TryGetValue(variable, out var space))
            {
                _storage[variable] = space = new PlcByteSpace(variable.Type.Size);
            }
            return new PlcLocation(space, 0, variable.Type);
        }

        internal void CountIndexError() => IndexErrors++;

        /// <summary>
        /// Cold start: unlocated variables start from their declared initial values
        /// (zero otherwise) when the project loads; located variables with a declared
        /// initial value get it in the data store on the first scan.
        /// </summary>
        private void InitializeVariables(bool located)
        {
            foreach (var variable in Project.Variables.Values)
            {
                if ((variable.Address != null) != located) continue;
                foreach (var init in variable.InitialValues)
                {
                    if (!PlcLiteral.TryParse(init.Text, out var value)) continue;
                    var reference = init.Path.Length == 0 ? variable.Name
                        : init.Path.StartsWith('[') ? variable.Name + init.Path
                        : variable.Name + "." + init.Path;
                    PlcOperandParser.Parse(reference, Project.Variables).Write(this, value);
                }
            }
        }

        private void RunSection(PlcSection section, PlcTask task)
        {
            if (section.Condition != null && !section.Condition.Read(this).AsBool())
            {
                section.ActiveLastScan = false;
                foreach (var block in section.Blocks) block.ExecutedLastScan = false;
                return;
            }

            section.ActiveLastScan = true;
            foreach (var block in section.Blocks)
            {
                Execute(block, task);
            }
        }

        private void Execute(PlcBlock block, PlcTask task)
        {
            // EN: a linked or assigned EN conditions the call; shown-but-free, hidden
            // or TRUE means unconditional (33004225, "Conditional/Unconditional FFB Call").
            if (block.En is { IsConnected: true } en)
            {
                var enabled = ReadLinkOrOperand(en);
                block.LastEn = enabled.AsBool();
                if (en.Inverted) enabled = PlcOps.Invert(enabled);
                if (!enabled.AsBool())
                {
                    Disable(block);
                    return;
                }
            }

            var call = block.Call;
            var fields = block.Instance != null ? InstanceFields(block) : null;
            var simulated = block.Behavior != null;
            for (var i = 0; i < block.Inputs.Count; i++)
            {
                var pin = block.Inputs[i];

                // A block that does not compute has no use for whole structures (DFB
                // in/out DDTs can be hundreds of registers); skip reading them.
                if (!simulated && pin.CarriesStructure)
                {
                    block.LastInputs[i] = default;
                    call.SetInput(i, default);
                    continue;
                }

                var raw = pin.IsConnected ? ReadLinkOrOperand(pin)
                    : fields?.Inputs[i] is { } field ? Memory.Read(field)
                    : default;
                block.LastInputs[i] = raw;
                var value = pin.Inverted ? PlcOps.Invert(raw) : raw;
                call.SetInput(i, value);

                // A function block instance keeps the parameters it was called with.
                if (pin.IsConnected && fields?.Inputs[i] is { } target) Memory.Write(target, value);
            }

            if (!simulated)
            {
                // Not simulated: the block keeps its outputs, as if it ran unchanged.
                block.LastEno = true;
                block.ExecutedLastScan = true;
                return;
            }

            call.BeginCall();
            call.State = block.State;
            call.Now = Now;
            call.ColdStart = _coldStart;
            call.FirstTaskCycle = task.FirstCycle;
            try
            {
                block.Behavior!.Execute(call);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                BlockErrors++;
                call.Fail();
            }
            block.State = call.State;

            if (call.Failed)
            {
                Disable(block);
                return;
            }

            for (var i = 0; i < block.Outputs.Count; i++)
            {
                if (!call.WasWritten(i)) continue;
                var pin = block.Outputs[i];
                var value = call.Output(i);
                if (fields?.Outputs[i] is { } field) Memory.Write(field, value);
                if (pin.Inverted) value = PlcOps.Invert(value);
                block.OutputLinks[i] = value;
                pin.Operand?.Write(this, value);
            }

            block.LastEno = true;
            WriteEno(block);
            block.ExecutedLastScan = true;
        }

        /// <summary>
        /// ENO = 0 (EN = 0, or an execution error): a function's output links go to 0,
        /// a function block's keep their last value; output variables keep theirs.
        /// </summary>
        private void Disable(PlcBlock block)
        {
            block.LastEno = false;
            block.ExecutedLastScan = false;
            if (!block.IsFunctionBlock)
            {
                for (var i = 0; i < block.OutputLinks.Length; i++)
                {
                    var current = block.OutputLinks[i];
                    block.OutputLinks[i] = current.HasValue ? PlcValue.DefaultOf(current.Type!) : PlcOps.False;
                }
            }
            WriteEno(block);
        }

        private void WriteEno(PlcBlock block)
        {
            if (block.Eno?.Operand is not { IsWritable: true } target) return;
            var value = PlcOps.FromBool(block.LastEno);
            target.Write(this, block.Eno.Inverted ? PlcOps.Invert(value) : value);
        }

        private PlcValue ReadLinkOrOperand(PlcBlockPin pin)
        {
            if (pin.SourceBlock is { } source)
            {
                if (pin.SourceOutput >= 0) return source.OutputLinks[pin.SourceOutput];
                var eno = PlcOps.FromBool(source.LastEno);
                return source.Eno?.Inverted == true ? PlcOps.Invert(eno) : eno;
            }
            return pin.Operand?.Read(this) ?? default;
        }

        /// <summary>
        /// Where a function block instance keeps each pin's parameter. A free input
        /// reads it (the declared initial value, or the last value it was given), as
        /// IEC 61131-3 keeps unassigned FB inputs; outputs are stored there so other
        /// code can read <c>Instance.Q</c>. Instance memory never moves, so this is
        /// worked out once per block.
        /// </summary>
        private PlcBlock.PinFields InstanceFields(PlcBlock block)
        {
            if (block.InstanceFields is { } known) return known;

            var instance = Locate(block.Instance!);
            PlcLocation? Field(PlcBlockPin pin) => instance.Type.FindField(pin.Name) is { } field
                ? new PlcLocation(instance.Space, instance.Offset + field.Offset, field.Type)
                : null;
            return block.InstanceFields = new PlcBlock.PinFields(
                block.Inputs.Select(Field).ToArray(),
                block.Outputs.Select(Field).ToArray());
        }
    }
}

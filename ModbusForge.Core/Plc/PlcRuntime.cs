using System;
using System.Collections.Generic;
using System.Linq;
using ModbusForge.Core.Plc.St;
using ModbusForge.Data;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Executes an imported Unity project the way the controller's tasks do: every
    /// scan runs each task's sections in order (FBD blocks in their execution order,
    /// ST statements top to bottom), and user function blocks run their own FBD/ST
    /// code per instance. Located variables live in the Modbus data store (the Quantum
    /// state RAM), so a scan reads and writes the registers other programs see;
    /// unlocated variables live in the runtime's own memory.
    /// </summary>
    /// <remarks>
    /// Blocks keep their scan state (timers, edges, link values) on the project model,
    /// so a project is run by one runtime at a time; compile it again for another.
    /// </remarks>
    public sealed class PlcRuntime
    {
        // %S bits the runtime drives (Control Expert System Bits and Words, EIO0000002135):
        // %S0 cold start, %S4-%S7 time bases, %S10 IOERR and %S16 IOERRTSK (normally 1,
        // 0 on an I/O error; the simulation has none), %S12 PLCRUNNING, %S13 first cycle
        // in RUN, %S20 INDEXOVF (set on an index overflow, reset by the application),
        // %S21 first task cycle.
        private const int ColdStartBit = 0;
        private const int TimeBase10MsBit = 4;
        private const int TimeBase100MsBit = 5;
        private const int TimeBase1SBit = 6;
        private const int TimeBase1MinBit = 7;
        private const int IoErrorBit = 10;
        private const int PlcRunningBit = 12;
        private const int FirstCycleInRunBit = 13;
        private const int TaskIoErrorBit = 16;
        private const int IndexOverflowBit = 20;
        private const int FirstTaskCycleBit = 21;

        /// <summary>DFBs nest in DFBs; deeper than this is treated as a runaway and stopped.</summary>
        private const int MaxDfbDepth = 32;

        private readonly Dictionary<PlcVariable, PlcByteSpace> _storage = new();
        private readonly PlcGlobalScope _globalScope;

        // Per instance (by its memory and type: a DFB's first field can be an instance
        // at the same offset): standard FB state for ST calls, compiled DFB code.
        private readonly Dictionary<(PlcSpace, int, PlcType), object?> _instanceStates = new();
        private readonly Dictionary<(PlcSpace, int, PlcType), DfbBody> _dfbBodies = new();
        private readonly Dictionary<PlcType, (PlcBlockBehavior? Behavior, PlcCall? Call, PlcField[] Inputs, PlcField[] Outputs)> _instanceCalls = new();
        private readonly HashSet<string> _reportedDfbProblems = new(StringComparer.OrdinalIgnoreCase);

        private double _clock;
        private bool _coldStart = true;
        private DataStore? _initializedStore;
        private PlcTask? _currentTask;
        private int _dfbDepth;

        public PlcRuntime(PlcProject project)
        {
            Project = project ?? throw new ArgumentNullException(nameof(project));
            Memory = new PlcMemory();
            _globalScope = new PlcGlobalScope(project.Variables);
            foreach (var block in project.Blocks) block.ResetRuntimeState();
            Memory.SystemBits.WriteBit(IoErrorBit, true);
            Memory.SystemBits.WriteBit(TaskIoErrorBit, true);
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

        /// <summary>ST run-time errors (division by zero, a loop cut off, a failed function) since the start.</summary>
        public int StErrors { get; private set; }

        /// <summary>What could not be compiled in DFB code, once per DFB type.</summary>
        public List<string> DfbProblems { get; } = new();

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
            Memory.BeginScan();
            var bits = Memory.SystemBits;
            bits.WriteBit(ColdStartBit, _coldStart);
            bits.WriteBit(FirstCycleInRunBit, _coldStart);
            bits.WriteBit(IoErrorBit, true);
            bits.WriteBit(PlcRunningBit, true);
            bits.WriteBit(TimeBase10MsBit, (Now / 5) % 2 == 1);
            bits.WriteBit(TimeBase100MsBit, (Now / 50) % 2 == 1);
            bits.WriteBit(TimeBase1SBit, (Now / 500) % 2 == 1);
            bits.WriteBit(TimeBase1MinBit, (Now / 30_000) % 2 == 1);

            foreach (var task in Project.Tasks)
            {
                _currentTask = task;
                bits.WriteBit(FirstTaskCycleBit, task.FirstCycle || _coldStart);
                foreach (var section in task.Sections)
                {
                    RunSection(section, task);
                }
                task.FirstCycle = false;
            }
            _currentTask = null;

            bits.WriteBit(FirstTaskCycleBit, false);
            bits.WriteBit(ColdStartBit, false);
            bits.WriteBit(FirstCycleInRunBit, false);
            _coldStart = false;
            ScanCount++;
        }

        /// <summary>Reads a variable, field or direct address by its Unity name ("Pump.Run", "%MW10").</summary>
        public PlcValue Read(string reference) => PlcOperandParser.Parse(reference, _globalScope).Read(this);

        /// <summary>Writes a variable, field or direct address by its Unity name.</summary>
        public void Write(string reference, PlcValue value) => PlcOperandParser.Parse(reference, _globalScope).Write(this, value);

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

        internal void CountIndexError()
        {
            IndexErrors++;
            Memory.SystemBits.WriteBit(IndexOverflowBit, true);
        }

        internal void CountStError() => StErrors++;

        /// <summary>
        /// Cold start: unlocated variables start from their type's declared defaults
        /// (a DFB's parameter initial values), then their own declared initial values,
        /// zero otherwise, when the project loads; located variables get theirs in the
        /// data store on the first scan.
        /// </summary>
        private void InitializeVariables(bool located)
        {
            foreach (var variable in Project.Variables.Values)
            {
                if ((variable.Address != null) != located) continue;
                if (variable.Type.HasDefaults) ApplyDefaults(Locate(variable), variable.Type, depth: 0);
                foreach (var init in variable.InitialValues)
                {
                    WriteInitial(Locate(variable), init);
                }
            }
        }

        private void ApplyDefaults(PlcLocation location, PlcType type, int depth)
        {
            if (depth > MaxDfbDepth) return;
            foreach (var field in type.Fields)
            {
                var fieldLocation = new PlcLocation(location.Space, location.Offset + field.Offset, field.Type);
                if (field.Type.HasDefaults) ApplyDefaults(fieldLocation, field.Type, depth + 1);
                foreach (var init in field.Defaults) WriteInitial(fieldLocation, init);
            }
        }

        /// <summary>Writes one declared initial value ("" = the location itself, "PT", "[3]", "a.b") below a location.</summary>
        private void WriteInitial(PlcLocation location, PlcInitialValue init)
        {
            if (!PlcLiteral.TryParse(init.Text, out var value)) return;
            if (init.Path.Length == 0)
            {
                Memory.Write(location, value);
                return;
            }

            var text = init.Path.StartsWith('[') ? "this" + init.Path : "this." + init.Path;
            PlcOperandParser.Parse(text, new ThisScope(location)).Write(this, value);
        }

        /// <summary>A scope with one name, "this", for paths relative to a location.</summary>
        private sealed class ThisScope : IPlcScope
        {
            private readonly PlcLocation _location;

            public ThisScope(PlcLocation location) => _location = location;

            public PlcRoot? Resolve(string name) => name == "this" ? new PlcFixedRoot(_location) : null;
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
            if (section.Program is { } program)
            {
                RunProgram(program);
                return;
            }

            var blocks = section.Blocks;
            for (var i = 0; i < blocks.Count; i++)
            {
                Execute(blocks[i], task);
            }
        }

        private void RunProgram(StProgram program)
        {
            try
            {
                program.Execute(this);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                // One statement failing must not stop the scan: the rest of this code is
                // skipped this cycle, as a controller stops a task section on an error.
                StErrors++;
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
            var fields = block.InstanceRoot != null ? InstanceFields(block) : null;
            var dfb = block.Kind == PlcBlockKind.UserFunctionBlock && fields?.Instance.Type.Dfb != null
                ? block.DfbCall ?? DfbCallFor(block, fields)
                : null;
            var simulated = block.Behavior != null || dfb != null;
            var inputs = block.InputPins;
            for (var i = 0; i < inputs.Length; i++)
            {
                var pin = inputs[i];

                // An in/out parameter wired to a variable: bound to it for this call, so
                // the DFB works on the variable itself (IEC 61131-3 VAR_IN_OUT is passed
                // by reference). Anything else is copied in and out through the field.
                if (dfb?.InputRoots[i] is { } inOut)
                {
                    if (!pin.IsLinked && pin.Operand is PlcReferenceOperand reference && reference.Resolve(this) is { IsValid: true } actual)
                    {
                        inOut.Bind(actual);
                        block.LastInputs[i] = pin.CarriesStructure ? default : Memory.Read(actual);
                        continue;
                    }
                    inOut.BindOwn();
                }

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

            if (dfb != null)
            {
                // The DFB's own code works on the instance; its outputs (and in/out
                // parameters not bound by reference, copied back) come out of its fields.
                RunDfbBody(dfb.Body);
                var dfbOutputs = block.OutputPins;
                for (var i = 0; i < dfbOutputs.Length; i++)
                {
                    var pin = dfbOutputs[i];
                    if (dfb.OutputRoots[i] is { ByReference: true } inOut)
                    {
                        // The DFB already worked on the caller's variable; a different
                        // variable on the output side gets its value copied to it
                        // (35006144, FBD "VAR_IN_OUT variable").
                        if (pin.Operand is PlcReferenceOperand output && output.Resolve(this) is { IsValid: true } target
                            && !target.SameAs(inOut.Current))
                        {
                            Memory.Write(target, Memory.Read(inOut.Current));
                        }
                        block.OutputLinks[i] = pin.CarriesStructure ? default : Memory.Read(inOut.Current);
                        continue;
                    }
                    if (fields!.Outputs[i] is not { } field) continue;
                    var value = Memory.Read(field);
                    if (pin.Inverted) value = PlcOps.Invert(value);
                    block.OutputLinks[i] = value;
                    pin.Operand?.Write(this, value);
                }
                block.LastEno = true;
                WriteEno(block);
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

            var outputs = block.OutputPins;
            for (var i = 0; i < outputs.Length; i++)
            {
                if (!call.WasWritten(i)) continue;
                var pin = outputs[i];
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
        private PlcBlock.PinFields InstanceFields(PlcBlock block) => block.InstanceFields ?? ResolveInstanceFields(block);

        // A separate method: C# allocates a closure when the method that declares the
        // captured variables is entered, so here only on a block's first execution.
        private PlcBlock.PinFields ResolveInstanceFields(PlcBlock block)
        {
            var instance = block.InstanceRoot!.Locate(this);
            PlcLocation? Field(PlcBlockPin pin) => instance.Type.FindField(pin.Name) is { } field
                ? new PlcLocation(instance.Space, instance.Offset + field.Offset, field.Type)
                : null;
            return block.InstanceFields = new PlcBlock.PinFields(
                instance,
                block.Inputs.Select(Field).ToArray(),
                block.Outputs.Select(Field).ToArray());
        }

        // ------------------------------------------------------------------
        // Function block instances called from ST, and DFB code
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs a function block instance whose inputs are already in its fields: a
        /// standard EFB (its state kept per instance) or a DFB's own code. Types the
        /// runtime cannot execute leave the instance as it is.
        /// </summary>
        internal void CallInstance(PlcLocation instance, DfbBody? body = null)
        {
            var type = instance.Type;
            if (type.Dfb != null)
            {
                RunDfbBody(body ?? DfbBodyFor(instance));
                return;
            }

            if (!_instanceCalls.TryGetValue(type, out var entry))
            {
                var behavior = PlcStandardLibrary.Find(type.Name) is { IsFunctionBlock: true } fb ? fb : null;
                var inputs = type.Fields.Where(f => f.Direction is PlcParameterDirection.Input or PlcParameterDirection.InOut).ToArray();
                var outputs = type.Fields.Where(f => f.Direction is PlcParameterDirection.Output or PlcParameterDirection.InOut).ToArray();
                var call = behavior != null ? new PlcCall(inputs.Select(f => f.Name).ToArray(), outputs.Select(f => f.Name).ToArray()) : null;
                _instanceCalls[type] = entry = (behavior, call, inputs, outputs);
            }
            if (entry.Behavior == null || entry.Call == null) return;

            var key = (instance.Space, instance.Offset, type);
            var fbCall = entry.Call;
            fbCall.BeginCall();
            for (var i = 0; i < entry.Inputs.Length; i++) fbCall.SetInput(i, Memory.Read(FieldOf(instance, entry.Inputs[i])));
            fbCall.State = _instanceStates.TryGetValue(key, out var state) ? state : null;
            fbCall.Now = Now;
            fbCall.ColdStart = _coldStart;
            fbCall.FirstTaskCycle = _currentTask?.FirstCycle ?? false;
            entry.Behavior.Execute(fbCall);
            _instanceStates[key] = fbCall.State;
            if (fbCall.Failed)
            {
                StErrors++;
                return;
            }
            for (var i = 0; i < entry.Outputs.Length; i++)
            {
                if (fbCall.WasWritten(i)) Memory.Write(FieldOf(instance, entry.Outputs[i]), fbCall.Output(i));
            }
        }

        private static PlcLocation FieldOf(PlcLocation instance, PlcField field)
            => new(instance.Space, instance.Offset + field.Offset, field.Type);

        /// <summary>
        /// A DFB instance's compiled code (per section, FBD blocks or ST statements) and
        /// its in/out parameters, which each call binds to that call's variables.
        /// </summary>
        internal sealed record DfbBody(
            (PlcBlock[]? Blocks, StProgram? Program)[] Sections,
            IReadOnlyDictionary<string, PlcInOutRoot> InOuts);

        /// <summary>A DFB called from an FBD block: its code, and per pin the in/out parameter it passes, if any.</summary>
        internal sealed record DfbCall(DfbBody Body, PlcInOutRoot?[] InputRoots, PlcInOutRoot?[] OutputRoots);

        /// <summary>A DFB instance's code, compiled on its first call.</summary>
        internal DfbBody DfbBodyFor(PlcLocation instance)
        {
            var key = (instance.Space, instance.Offset, instance.Type);
            if (!_dfbBodies.TryGetValue(key, out var body))
            {
                _dfbBodies[key] = body = CompileDfb(instance);
            }
            return body;
        }

        /// <summary>What an FBD block's DFB call needs, worked out on its first execution.</summary>
        private DfbCall DfbCallFor(PlcBlock block, PlcBlock.PinFields fields)
        {
            var body = DfbBodyFor(fields.Instance);
            PlcInOutRoot? RootOf(PlcBlockPin pin) => body.InOuts.TryGetValue(pin.Name, out var root) ? root : null;
            return block.DfbCall = new DfbCall(body, block.InputPins.Select(RootOf).ToArray(), block.OutputPins.Select(RootOf).ToArray());
        }

        internal void RunDfbBody(DfbBody body)
        {
            if (_dfbDepth >= MaxDfbDepth)
            {
                StErrors++;
                return;
            }

            _dfbDepth++;
            try
            {
                var task = _currentTask ?? (Project.Tasks.Count > 0 ? Project.Tasks[0] : DefaultTask);
                foreach (var (blocks, program) in body.Sections)
                {
                    if (program != null)
                    {
                        RunProgram(program);
                        continue;
                    }
                    foreach (var block in blocks!)
                    {
                        Execute(block, task);
                    }
                }
            }
            finally
            {
                _dfbDepth--;
            }
        }

        private static readonly PlcTask DefaultTask = new("MAST", "cyclic", Array.Empty<PlcSection>());

        /// <summary>
        /// Compiles a DFB's code for one instance: its names resolve to that instance's
        /// memory, and its nested function blocks get their own state. Done once per
        /// instance, on its first call.
        /// </summary>
        private DfbBody CompileDfb(PlcLocation instance)
        {
            var definition = instance.Type.Dfb!;
            var inOuts = new Dictionary<string, PlcInOutRoot>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in instance.Type.Fields)
            {
                if (field.Direction == PlcParameterDirection.InOut) inOuts[field.Name] = new PlcInOutRoot(FieldOf(instance, field));
            }
            var scope = new PlcInstanceScope(instance, _globalScope, inOuts);
            var types = Project.Types;
            var problems = new List<string>();
            var sections = new List<(PlcBlock[]?, StProgram?)>();
            foreach (var section in definition.Sections)
            {
                if (section.Language == "FBD" && section.Fbd != null && types != null)
                {
                    sections.Add((new PlcFbdCompiler(types, scope, problems).CompileSource(section.Fbd, section.Name).ToArray(), null));
                }
                else if (section.Language == "ST" && section.Text != null)
                {
                    var program = new StCompiler(scope, types).Compile(section.Text);
                    problems.AddRange(program.Problems);
                    sections.Add((null, program));
                }
                else
                {
                    problems.Add($"section {section.Name} is {section.Language}, which the runtime does not execute");
                }
            }

            if (problems.Count > 0 && _reportedDfbProblems.Add(definition.TypeName))
            {
                DfbProblems.AddRange(problems.Select(p => $"{definition.TypeName}: {p}"));
            }
            return new DfbBody(sections.ToArray(), inOuts);
        }
    }
}

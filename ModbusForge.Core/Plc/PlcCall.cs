using System;
using System.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// One execution of a block: the values at its input pins (after pin
    /// negation), the outputs it produces, its instance state and the controller
    /// clock. Pins are matched by their formal names, case-insensitively.
    /// </summary>
    public sealed class PlcCall
    {
        private readonly string[] _inputNames;
        private readonly PlcValue[] _inputs;
        private readonly string[] _outputNames;
        private readonly PlcValue[] _outputs;
        private readonly bool[] _written;
        private readonly PlcType?[] _outputTypes;

        public PlcCall(string[] inputNames, string[] outputNames)
        {
            // Interned upper-case names: the behaviours' literal pin names ("IN", "Q")
            // then match by reference, without a string comparison.
            inputNames = inputNames.Select(n => string.Intern(n.ToUpperInvariant())).ToArray();
            outputNames = outputNames.Select(n => string.Intern(n.ToUpperInvariant())).ToArray();
            _inputNames = inputNames;
            _inputs = new PlcValue[inputNames.Length];
            _outputNames = outputNames;
            _outputs = new PlcValue[outputNames.Length];
            _written = new bool[outputNames.Length];
            _outputTypes = new PlcType?[outputNames.Length];
        }

        /// <summary>
        /// The type of the variable an output is written to, when known: a generic
        /// output (MOVE_INT_ARINT's table) takes its shape from it.
        /// </summary>
        public PlcType? OutputType(string name)
        {
            var index = IndexOf(_outputNames, name);
            return index >= 0 ? _outputTypes[index] : null;
        }

        internal void SetOutputType(int index, PlcType? type) => _outputTypes[index] = type;

        /// <summary>Per-instance state of a function block (timers, edges, counters).</summary>
        public object? State { get; set; }

        /// <summary>Controller time in milliseconds since the runtime started.</summary>
        public long Now { get; internal set; }

        /// <summary>True during the first cycle after a cold start.</summary>
        public bool ColdStart { get; internal set; }

        /// <summary>True during the first cycle of the task after it (re)starts.</summary>
        public bool FirstTaskCycle { get; internal set; }

        /// <summary>Set by the block when execution fails (ENO becomes FALSE).</summary>
        public bool Failed { get; private set; }

        public int InputCount => _inputNames.Length;
        public int OutputCount => _outputNames.Length;

        public string InputName(int index) => _inputNames[index];
        public string OutputName(int index) => _outputNames[index];

        public PlcValue Input(int index) => _inputs[index];

        /// <summary>All input values in pin order.</summary>
        public ReadOnlySpan<PlcValue> Inputs => _inputs;

        /// <summary>The value at the named input; "no value" when the block has no such pin.</summary>
        public PlcValue Input(string name)
        {
            var index = IndexOf(_inputNames, name);
            return index >= 0 ? _inputs[index] : default;
        }

        public bool HasInput(string name) => IndexOf(_inputNames, name) >= 0;

        public void SetOutput(string name, PlcValue value)
        {
            var index = IndexOf(_outputNames, name);
            if (index < 0) return;
            _outputs[index] = value;
            _written[index] = true;
        }

        public void SetOutput(int index, PlcValue value)
        {
            _outputs[index] = value;
            _written[index] = true;
        }

        public void Fail() => Failed = true;

        internal void SetInput(int index, PlcValue value) => _inputs[index] = value;

        internal PlcValue Output(int index) => _outputs[index];

        internal bool WasWritten(int index) => _written[index];

        internal void BeginCall()
        {
            Failed = false;
            Array.Clear(_written);
        }

        private static int IndexOf(string[] names, string name)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (ReferenceEquals(names[i], name)) return i;
            }
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
    }

    /// <summary>What a block type does when it executes.</summary>
    public abstract class PlcBlockBehavior
    {
        /// <summary>
        /// Function blocks keep their output links when EN is 0; functions drive them
        /// to 0 (Unity Pro, EN and ENO: 33004225 p. 15-16).
        /// </summary>
        public abstract bool IsFunctionBlock { get; }

        public virtual object? CreateState() => null;

        public abstract void Execute(PlcCall call);
    }
}

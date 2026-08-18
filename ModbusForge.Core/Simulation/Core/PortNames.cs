namespace ModbusForge.Core.Simulation.Core
{
    /// <summary>
    /// Canonical port names declared by the function blocks, in IEC 61131-3 style:
    /// boolean outputs are "Q", timer inputs "IN", counter inputs "CU"/"CD",
    /// latch set/reset "S"/"R", binary operands "A"/"B", and analog values "Value".
    ///
    /// The visual editor exposes only three generic connector slots ("Input1",
    /// "Input2", "Output"); those map onto the declared ports positionally (see
    /// <see cref="BlockPorts"/> and the execution engine), so the wire format and
    /// saved programs are unaffected by what a port is called. The names here are
    /// what the user sees on the canvas pins and in the secondary-output display.
    /// </summary>
    public static class PortNames
    {
        // Inputs
        public const string TimerInput = "IN";
        public const string GateInput1 = "IN1";
        public const string GateInput2 = "IN2";
        public const string LatchSet = "S";
        public const string LatchReset = "R";
        public const string CountUp = "CU";
        public const string CountDown = "CD";
        public const string OperandA = "A";
        public const string OperandB = "B";
        public const string Value = "Value";
        public const string Start = "Start";
        public const string Stop = "Stop";
        public const string OpenCmd = "OpenCmd";
        public const string CloseCmd = "CloseCmd";
        public const string Run = "Run";
        public const string SpeedReference = "SpeedReference";

        // Outputs
        public const string BoolOutput = "Q";
        public const string MotorRun = "Run";
        public const string ValveOpen = "Open";
        public const string Fault = "Fault";
        public const string VsdRunning = "Running";
        public const string SpeedFeedback = "SpeedFeedback";
        public const string AtSpeed = "AtSpeed";
    }
}

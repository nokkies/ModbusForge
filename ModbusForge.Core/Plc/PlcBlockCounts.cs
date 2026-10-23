using System.Collections.Generic;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// How much of a project the PLC runtime computes. Each block counts once, the way the
    /// canvas notes it: computed, else an instance of a protected DFB (its code is encrypted,
    /// so it stays empty), else an unsupported type the runtime does not simulate.
    /// </summary>
    public readonly record struct PlcBlockCounts(int Total, int Computed, int EmptyProtectedDfb, int Unsupported)
    {
        /// <summary>Counts the given blocks.</summary>
        public static PlcBlockCounts Of(IEnumerable<PlcBlock> blocks)
        {
            var total = 0;
            var computed = 0;
            var emptyProtectedDfb = 0;
            var unsupported = 0;
            foreach (var block in blocks)
            {
                total++;
                if (block.IsSimulated) computed++;
                else if (block.IsProtectedDfb) emptyProtectedDfb++;
                else unsupported++;
            }
            return new PlcBlockCounts(total, computed, emptyProtectedDfb, unsupported);
        }

        /// <summary>
        /// The status wording, for example "1 of 3 blocks computed, 1 empty (protected DFB)".
        /// The two parts after the first appear only when their count is above 0.
        /// </summary>
        public string Describe()
        {
            var text = $"{Computed} of {Total} blocks computed";
            if (EmptyProtectedDfb > 0) text += $", {EmptyProtectedDfb} empty (protected DFB)";
            if (Unsupported > 0) text += $", {Unsupported} not simulated (unsupported type)";
            return text;
        }
    }
}

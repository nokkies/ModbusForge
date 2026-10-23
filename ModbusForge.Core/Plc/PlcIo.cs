using System;
using System.Collections.Generic;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// A module of the Quantum I/O map (an XEF's IOConf): where it sits, the first state
    /// RAM word its inputs and outputs are given (inputRefOffset, outputRefOffset), and
    /// the parameter words Control Expert configures it with.
    /// </summary>
    /// <param name="TopologicalAddress">\bus.drop\rack.slot, such as \1.1\1.8 (local bus, drop 1, rack 1, slot 8).</param>
    public sealed record PlcIoModule(string TopologicalAddress, string PartNumber, int InputStart, int OutputStart, IReadOnlyList<int> Parameters);

    /// <summary>
    /// The controller's I/O as the I/O configuration blocks (QUANTUM, AVI030) and the
    /// scaling blocks reach it: the modules of the project's I/O map, and the input words
    /// those modules fill in the state RAM (in the simulation, whoever writes the input
    /// registers).
    /// </summary>
    public sealed class PlcIo
    {
        // The local rack is rack 1 of drop 1 on the local bus.
        private const string LocalRack = @"\1.1\1.";

        private readonly Dictionary<int, PlcIoModule> _localSlots = new();
        private readonly PlcMemory _memory;

        internal PlcIo(IEnumerable<PlcIoModule> modules, PlcMemory memory)
        {
            _memory = memory;
            foreach (var module in modules)
            {
                if (module.TopologicalAddress.StartsWith(LocalRack, StringComparison.Ordinal)
                    && int.TryParse(module.TopologicalAddress.AsSpan(LocalRack.Length), out var slot))
                {
                    _localSlots.TryAdd(slot, module);
                }
            }
        }

        /// <summary>The module in <paramref name="slot"/> of the local rack; null when the slot is empty.</summary>
        public PlcIoModule? LocalModule(int slot) => _localSlots.GetValueOrDefault(slot);

        /// <summary>Input word %IW<paramref name="number"/> (a 3x register), as the unsigned 16 bits in it.</summary>
        public int InputWord(int number)
            => number < 1 ? 0 : (int)_memory.Read(new PlcLocation(_memory.InputRegisters, number * 2, PlcType.Word)).AsInteger();
    }
}

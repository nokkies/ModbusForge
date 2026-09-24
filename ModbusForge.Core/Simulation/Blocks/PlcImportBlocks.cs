using System;
using System.Collections.Generic;
using ModbusForge.Core.Simulation.Core;

namespace ModbusForge.Core.Simulation.Blocks
{
    /// <summary>
    /// Engine stand-in for imported Unity Pro content that has no simulation
    /// behaviour: an FFB with no ModbusForge equivalent, or a section text box.
    /// It has no ports and executes nothing, so the graph loads and runs while
    /// downstream blocks never see invented values.
    /// </summary>
    public sealed class PlcInertBlock : IFunctionBlock
    {
        public PlcInertBlock(string typeId, string displayName)
        {
            TypeId = typeId;
            DisplayName = displayName;
        }

        public string TypeId { get; }

        public string DisplayName { get; }

        public string Category => "PLC";

        public IReadOnlyList<IPort> Ports { get; } = Array.Empty<IPort>();

        public IReadOnlyList<BlockParameterDescriptor> Parameters => BooleanLogicBlock.EmptyParameters;

        public void Execute(IExecutionContext context)
        {
        }
    }
}

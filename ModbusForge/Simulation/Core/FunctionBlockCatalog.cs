using System;
using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Catalog of known function block types. Provides discovery and instantiation.
    /// </summary>
    public sealed class FunctionBlockCatalog
    {
        private readonly Dictionary<string, IFunctionBlock> _blocks = new(StringComparer.Ordinal);

        public IReadOnlyCollection<FunctionBlockDescriptor> Descriptors =>
            _blocks.Values.Select(b => new FunctionBlockDescriptor(b.TypeId, b.DisplayName, b.Category, b.Ports)).ToList();

        public void Register(IFunctionBlock block)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));

            _blocks[block.TypeId] = block;
        }

        public IFunctionBlock Create(string typeId)
        {
            if (!_blocks.TryGetValue(typeId, out var prototype))
                throw new InvalidOperationException($"Unknown function block type '{typeId}'.");

            // Blocks are stateless prototypes; callers are responsible for holding instance state.
            return prototype;
        }

        public FunctionBlockDescriptor? GetDescriptor(string typeId)
        {
            return _blocks.TryGetValue(typeId, out var block)
                ? new FunctionBlockDescriptor(block.TypeId, block.DisplayName, block.Category, block.Ports)
                : null;
        }

        public bool Contains(string typeId)
        {
            return _blocks.ContainsKey(typeId);
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class VisualNodeEditorViewModel
    {
        private TagService? _tagService;

        /// <summary>
        /// Locates a tag by its unique identifier.
        /// </summary>
        public Tag? FindTagById(string id)
        {
            if (string.IsNullOrEmpty(id) || _tagService == null)
                return null;

            return _tagService.Tags.FirstOrDefault(t => t.Id == id);
        }

        /// <summary>
        /// Binds a tag's Modbus address to a node's input (or output, if the node is an output element).
        /// </summary>
        public void BindTagToNodeInput(VisualNode? node, Tag? tag, string? connector)
        {
            if (node == null || tag == null)
                return;

            var address = new PlcAddressReference
            {
                Area = tag.Area,
                Address = tag.Address,
                SymbolicName = tag.Name
            };

            var descriptor = NodeDescriptors.Get(node.ElementType);
            if (descriptor.IsOutput)
            {
                node.OutputAddress = address;
                StatusText = $"Bound tag '{tag.Name}' to {node.Name} output";
            }
            else
            {
                if (string.Equals(connector, "Input2", StringComparison.OrdinalIgnoreCase) && node.HasSecondInput)
                {
                    node.Input2Address = address;
                    StatusText = $"Bound tag '{tag.Name}' to {node.Name} Input2";
                }
                else
                {
                    node.Input1Address = address;
                    StatusText = $"Bound tag '{tag.Name}' to {node.Name} Input1";
                }
            }
        }

        /// <summary>
        /// Creates a new input node at the specified location and pre-binds it to the tag.
        /// </summary>
        public VisualNode? AddNodeForTagAt(Tag? tag, double x, double y)
        {
            if (tag == null)
                return null;

            var elementType = GetElementTypeForTag(tag);
            var node = AddNodeAt(elementType, x, y);
            if (node != null)
            {
                BindTagToNodeInput(node, tag, "Input1");
            }

            return node;
        }

        /// <summary>
        /// Adds the selected node's Input1Address (or a synthetic tag from the current live value)
        /// to the Watch window and brings the window to the front.
        /// </summary>
        private async Task AddSelectedNodeToWatchAsync()
        {
            if (SelectedNode == null || _tagService == null)
                return;

            try
            {
                await _tagService.InitializeAsync();
                var tag = await GetOrCreateTagForNodeAsync(SelectedNode);
                if (tag == null)
                    return;

                _tagService.AddToWatch(tag.Id);
                _tagWindowService?.ShowWatchWindow();
                StatusText = $"Added '{tag.Name}' to watch";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                StatusText = $"Add to watch failed: {ex.Message}";
            }
        }

        private async Task<Tag?> GetOrCreateTagForNodeAsync(VisualNode node)
        {
            var address = node.Input1Address;
            if (address != null && address.Address >= 0)
            {
                var existing = _tagService!.GetTagByAddress(address.Area, address.Address);
                if (existing != null)
                    return existing;

                return await _tagService.CreateTag(
                    GetUniqueTagName(node.Name),
                    "Default",
                    address.Area,
                    address.Address,
                    GetTagDataTypeForNode(node));
            }

            // Fallback: create a synthetic tag that captures the current live value.
            var fallbackAddress = 1;
            if (double.IsFinite(node.CurrentValueDouble) && node.CurrentValueDouble >= 0)
                fallbackAddress = Math.Max(1, (int)node.CurrentValueDouble);

            var newTag = await _tagService!.CreateTag(
                GetUniqueTagName(node.Name),
                "Default",
                PlcArea.HoldingRegister,
                fallbackAddress,
                TagDataType.Double);

            newTag.CurrentValue = node.CurrentValueDouble;
            return newTag;
        }

        private string GetUniqueTagName(string baseName)
        {
            if (_tagService == null)
                return baseName;

            var name = baseName;
            var counter = 1;
            while (_tagService.GetTagByName(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }

            return name;
        }

        private static PlcElementType GetElementTypeForTag(Tag tag)
        {
            if (tag.DataType == TagDataType.Bool || tag.Area is PlcArea.Coil or PlcArea.DiscreteInput)
                return PlcElementType.InputBool;

            if (tag.Area == PlcArea.HoldingRegister || tag.Area == PlcArea.InputRegister)
                return PlcElementType.InputInt;

            // Default to boolean input for unknown/undefined areas.
            return PlcElementType.InputBool;
        }

        private static TagDataType GetTagDataTypeForNode(VisualNode node)
        {
            if (node.ElementType is PlcElementType.InputBool or PlcElementType.OutputBool)
                return TagDataType.Bool;

            if (node.ElementType is PlcElementType.InputInt or PlcElementType.OutputInt)
                return TagDataType.UInt16;

            return TagDataType.Double;
        }
    }
}

using System.Windows;
using ModbusForge.Models;

namespace ModbusForge.Services.EditorCommands
{
    public class MoveNodeCommand : IEditorCommand
    {
        private readonly VisualNode _node;
        private readonly Point _oldPosition;
        private readonly Point _newPosition;

        public MoveNodeCommand(VisualNode node, Point oldPosition, Point newPosition)
        {
            _node = node;
            _oldPosition = oldPosition;
            _newPosition = newPosition;
        }

        public void Execute()
        {
            _node.X = _newPosition.X;
            _node.Y = _newPosition.Y;
        }

        public void Unexecute()
        {
            _node.X = _oldPosition.X;
            _node.Y = _oldPosition.Y;
        }
    }
}

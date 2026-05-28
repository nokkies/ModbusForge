using System.Linq;
using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.Services.EditorCommands
{
    public class AddConnectionCommand : IEditorCommand
    {
        private readonly VisualNodeEditorViewModel _viewModel;
        private readonly NodeConnection _connection;

        public AddConnectionCommand(VisualNodeEditorViewModel viewModel, NodeConnection connection)
        {
            _viewModel = viewModel;
            _connection = connection;
        }

        public void Execute()
        {
            var sourceNode = _viewModel.Nodes.FirstOrDefault(n => n.Id == _connection.SourceNodeId);
            var targetNode = _viewModel.Nodes.FirstOrDefault(n => n.Id == _connection.TargetNodeId);

            if (sourceNode != null && targetNode != null)
            {
                // Update connection points first
                _connection.StartX = sourceNode.X + sourceNode.Width - 6;
                _connection.StartY = sourceNode.Y + sourceNode.Height / 2;
                _connection.EndX = targetNode.X + 6;
                _connection.EndY = targetNode.Y + targetNode.Height / 2;
            }

            _viewModel.Connections.Add(_connection);
        }

        public void Unexecute()
        {
            _viewModel.Connections.Remove(_connection);
        }
    }
}

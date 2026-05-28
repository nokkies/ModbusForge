using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.Services.EditorCommands
{
    public class DeleteConnectionCommand : IEditorCommand
    {
        private readonly VisualNodeEditorViewModel _viewModel;
        private readonly NodeConnection _connection;

        public DeleteConnectionCommand(VisualNodeEditorViewModel viewModel, NodeConnection connection)
        {
            _viewModel = viewModel;
            _connection = connection;
        }

        public void Execute()
        {
            _viewModel.Connections.Remove(_connection);
        }

        public void Unexecute()
        {
            _viewModel.Connections.Add(_connection);
        }
    }
}

using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.Services.EditorCommands
{
    public class AddNodeCommand : IEditorCommand
    {
        private readonly VisualNodeEditorViewModel _viewModel;
        private readonly VisualNode _node;
        private readonly ProgramModel? _program;

        public AddNodeCommand(VisualNodeEditorViewModel viewModel, VisualNode node, ProgramModel? program)
        {
            _viewModel = viewModel;
            _node = node;
            _program = program;
        }

        public void Execute()
        {
            _viewModel.Nodes.Add(_node);

            if (_program != null)
            {
                _program.Nodes.Add(_node);
            }

            _viewModel.SelectedNode = _node;
        }

        public void Unexecute()
        {
            _viewModel.Nodes.Remove(_node);

            if (_program != null)
            {
                _program.Nodes.Remove(_node);
            }

            if (_viewModel.SelectedNode == _node)
            {
                _viewModel.SelectedNode = null;
            }
        }
    }
}

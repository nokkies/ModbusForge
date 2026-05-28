using System.Collections.Generic;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.Services.EditorCommands
{
    public class DeleteNodeCommand : IEditorCommand
    {
        private readonly VisualNodeEditorViewModel _viewModel;
        private readonly VisualNode _node;
        private readonly ProgramModel? _program;
        private readonly List<NodeConnection> _removedConnections;
        private readonly List<ConnectorConfiguration> _removedConfigs;

        public DeleteNodeCommand(VisualNodeEditorViewModel viewModel, VisualNode node, ProgramModel? program)
        {
            _viewModel = viewModel;
            _node = node;
            _program = program;

            _removedConnections = _viewModel.Connections
                .Where(c => c.SourceNodeId == node.Id || c.TargetNodeId == node.Id)
                .ToList();

            _removedConfigs = _viewModel.ConnectorConfigs
                .Where(c => c.NodeId == node.Id)
                .ToList();
        }

        public void Execute()
        {
            foreach (var connection in _removedConnections)
            {
                _viewModel.Connections.Remove(connection);
            }

            foreach (var config in _removedConfigs)
            {
                _viewModel.ConnectorConfigs.Remove(config);
            }

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

        public void Unexecute()
        {
            _viewModel.Nodes.Add(_node);

            if (_program != null)
            {
                _program.Nodes.Add(_node);
            }

            foreach (var config in _removedConfigs)
            {
                _viewModel.ConnectorConfigs.Add(config);
            }

            foreach (var connection in _removedConnections)
            {
                _viewModel.Connections.Add(connection);
            }
        }
    }
}

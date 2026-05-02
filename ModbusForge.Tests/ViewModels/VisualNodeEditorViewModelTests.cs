using System.Linq;
using ModbusForge.Models;
using ModbusForge.ViewModels;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class VisualNodeEditorViewModelTests
    {
        [Fact]
        public void SelectNode_WhenNoNodeSelected_ShouldSelectNode()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();
            var node = new VisualNode { Name = "Test Node" };
            viewModel.Nodes.Add(node);

            // Act
            viewModel.SelectNode(node);

            // Assert
            Assert.True(node.IsSelected);
            Assert.Equal(node, viewModel.SelectedNode);
        }

        [Fact]
        public void SelectNode_WhenAnotherNodeSelected_ShouldDeselectPreviousAndSelectNew()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();
            var node1 = new VisualNode { Name = "Node 1" };
            var node2 = new VisualNode { Name = "Node 2" };
            viewModel.Nodes.Add(node1);
            viewModel.Nodes.Add(node2);

            viewModel.SelectNode(node1);
            Assert.True(node1.IsSelected);
            Assert.Equal(node1, viewModel.SelectedNode);

            // Act
            viewModel.SelectNode(node2);

            // Assert
            Assert.False(node1.IsSelected);
            Assert.True(node2.IsSelected);
            Assert.Equal(node2, viewModel.SelectedNode);
        }

        [Fact]
        public void SelectNode_WithNull_ShouldClearSelection()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();
            var node = new VisualNode { Name = "Test Node" };
            viewModel.Nodes.Add(node);
            viewModel.SelectNode(node);

            Assert.True(node.IsSelected);
            Assert.Equal(node, viewModel.SelectedNode);

            // Act
            viewModel.SelectNode(null!);

            // Assert
            Assert.False(node.IsSelected);
            Assert.Null(viewModel.SelectedNode);
        }

        [Fact]
        public void ClearSelection_ShouldDeselectAllNodesAndClearSelectedNode()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();
            var node1 = new VisualNode { Name = "Node 1" };
            var node2 = new VisualNode { Name = "Node 2" };
            viewModel.Nodes.Add(node1);
            viewModel.Nodes.Add(node2);

            node1.IsSelected = true;
            node2.IsSelected = true;
            viewModel.SelectedNode = node1;

            // Act
            viewModel.ClearSelection();

            // Assert
            Assert.False(node1.IsSelected);
            Assert.False(node2.IsSelected);
            Assert.Null(viewModel.SelectedNode);
        }
    }
}

using System.Collections.Generic;
using ModbusForge.ViewModels;
using ModbusForge.Views;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class VisualNodeEditorSnapTests
    {
        [Fact]
        public void SnapToGrid_DefaultValue_Is_True()
        {
            var viewModel = new VisualNodeEditorViewModel();
            Assert.True(viewModel.SnapToGrid);
            Assert.Equal(20, viewModel.GridSize);
        }

        [Theory]
        [InlineData(0, 20, 0)]
        [InlineData(9, 20, 0)]
        [InlineData(10, 20, 20)]
        [InlineData(19, 20, 20)]
        [InlineData(20, 20, 20)]
        [InlineData(-5, 20, 0)]
        public void SnapToGrid_HelperMethod_ReturnsExpectedValues(double input, int gridSize, double expected)
        {
            var result = VisualNodeEditor.SnapToGrid(input, gridSize);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void AlignLeftCommand_NoSelection_IsNoOp()
        {
            var viewModel = new VisualNodeEditorViewModel();
            viewModel.SelectedNode = null;

            // This should not throw any exception
            var exception = Record.Exception(() => viewModel.AlignLeftCommand.Execute(null));
            Assert.Null(exception);
        }

        [Fact]
        public void AlignLeftCommand_OneSelectedNode_IsNoOp()
        {
            var viewModel = new VisualNodeEditorViewModel();
            var node = new VisualNode { X = 10, Y = 10 };
            viewModel.SelectedNode = node;

            viewModel.AlignLeftCommand.Execute(null);

            // Verify position didn't change (no-op)
            Assert.Equal(10, node.X);
            Assert.Equal(10, node.Y);
        }

        [Fact]
        public void DistributeHorizontallyCommand_LessThanTwoNodes_IsNoOp()
        {
            var viewModel = new VisualNodeEditorViewModel();

            // Case 1: 0 nodes
            viewModel.SelectedNode = null;
            viewModel.DistributeHorizontallyCommand.Execute(null);

            // Case 2: 1 node
            var node = new VisualNode { X = 10, Y = 10 };
            viewModel.SelectedNode = node;
            viewModel.DistributeHorizontallyCommand.Execute(null);

            // Position should not change
            Assert.Equal(10, node.X);
            Assert.Equal(10, node.Y);
        }
    }
}

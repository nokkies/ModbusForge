using System.Linq;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    /// <summary>
    /// Test double for <see cref="ITagWindowService"/> that records calls without
    /// creating any real WPF windows.
    /// </summary>
    internal sealed class StubTagWindowService : ITagWindowService
    {
        public int ShowTagBrowserCallCount { get; private set; }
        public int ShowWatchWindowCallCount { get; private set; }

        public void ShowTagBrowser() => ShowTagBrowserCallCount++;
        public void ShowWatchWindow() => ShowWatchWindowCallCount++;
    }

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

        // ------------------------------------------------------------------
        // ITagWindowService tests — no real WPF windows created
        // ------------------------------------------------------------------

        [Fact]
        public void OpenTagBrowserCommand_WhenTagWindowServiceInjected_CallsShowTagBrowser()
        {
            // Arrange
            var stub = new StubTagWindowService();
            var viewModel = new VisualNodeEditorViewModel(tagWindowService: stub);

            // Act
            viewModel.OpenTagBrowserCommand.Execute(null);

            // Assert
            Assert.Equal(1, stub.ShowTagBrowserCallCount);
            Assert.Equal(0, stub.ShowWatchWindowCallCount);
        }

        [Fact]
        public void OpenWatchWindowCommand_WhenTagWindowServiceInjected_CallsShowWatchWindow()
        {
            // Arrange
            var stub = new StubTagWindowService();
            var viewModel = new VisualNodeEditorViewModel(tagWindowService: stub);

            // Act
            viewModel.OpenWatchWindowCommand.Execute(null);

            // Assert
            Assert.Equal(0, stub.ShowTagBrowserCallCount);
            Assert.Equal(1, stub.ShowWatchWindowCallCount);
        }

        [Fact]
        public void OpenTagBrowserCommand_ExecutedTwice_CallsShowTagBrowserTwice()
        {
            // Arrange
            var stub = new StubTagWindowService();
            var viewModel = new VisualNodeEditorViewModel(tagWindowService: stub);

            // Act
            viewModel.OpenTagBrowserCommand.Execute(null);
            viewModel.OpenTagBrowserCommand.Execute(null);

            // Assert
            Assert.Equal(2, stub.ShowTagBrowserCallCount);
        }

        [Fact]
        public void OpenTagBrowserCommand_WhenNoTagWindowServiceRegistered_DoesNotThrow()
        {
            // Arrange — no ITagWindowService injected (simulates misconfigured DI or test env)
            var viewModel = new VisualNodeEditorViewModel();

            // Act & Assert — should log a warning and return, not throw
            var ex = Record.Exception(() => viewModel.OpenTagBrowserCommand.Execute(null));
            Assert.Null(ex);
        }

        [Fact]
        public void OpenWatchWindowCommand_WhenNoTagWindowServiceRegistered_DoesNotThrow()
        {
            // Arrange — no ITagWindowService injected
            var viewModel = new VisualNodeEditorViewModel();

            // Act & Assert
            var ex = Record.Exception(() => viewModel.OpenWatchWindowCommand.Execute(null));
            Assert.Null(ex);
        }
    }
}

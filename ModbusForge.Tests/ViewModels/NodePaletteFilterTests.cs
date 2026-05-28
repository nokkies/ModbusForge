using System.Linq;
using ModbusForge.ViewModels;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class NodePaletteFilterTests
    {
        [Fact]
        public void SearchEmpty_ShowsAllNodes()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();

            // Act
            viewModel.SearchText = "";

            // Assert
            Assert.All(viewModel.PaletteCategories, c => Assert.True(c.IsVisible));
            Assert.All(viewModel.PaletteCategories.SelectMany(c => c.Nodes), n => Assert.True(n.IsVisible));
        }

        [Fact]
        public void SearchSubstring_MatchesNodeNameOrCategory()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();

            // Act
            viewModel.SearchText = "timer";

            // Assert
            var timersCategory = viewModel.PaletteCategories.FirstOrDefault(c => c.Name == "Timers");
            var ioCategory = viewModel.PaletteCategories.FirstOrDefault(c => c.Name == "I/O");

            Assert.NotNull(timersCategory);
            Assert.True(timersCategory.IsVisible);
            Assert.All(timersCategory.Nodes, n => Assert.True(n.IsVisible)); // TON, TOF, TP all have "Timer" in name

            Assert.NotNull(ioCategory);
            Assert.False(ioCategory.IsVisible);
            Assert.All(ioCategory.Nodes, n => Assert.False(n.IsVisible));
        }

        [Fact]
        public void SearchCaseInsensitive_Matches()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();

            // Act
            viewModel.SearchText = "mAtH";

            // Assert
            var mathCategory = viewModel.PaletteCategories.FirstOrDefault(c => c.Name == "Math Operations");
            Assert.NotNull(mathCategory);
            Assert.True(mathCategory.IsVisible);
            Assert.All(mathCategory.Nodes, n => Assert.True(n.IsVisible)); // All match because category matches

            var logicCategory = viewModel.PaletteCategories.FirstOrDefault(c => c.Name == "Logic Gates");
            Assert.NotNull(logicCategory);
            Assert.False(logicCategory.IsVisible);
        }

        [Fact]
        public void SearchNoMatch_ShowsEmpty()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();

            // Act
            viewModel.SearchText = "xyz123";

            // Assert
            Assert.All(viewModel.PaletteCategories, c => Assert.False(c.IsVisible));
            Assert.All(viewModel.PaletteCategories.SelectMany(c => c.Nodes), n => Assert.False(n.IsVisible));
        }

        [Fact]
        public void SearchClear_RestoresAll()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();
            viewModel.SearchText = "logic";

            // Verify it filtered
            Assert.Contains(viewModel.PaletteCategories, c => !c.IsVisible);

            // Act
            viewModel.SearchText = "";

            // Assert
            Assert.All(viewModel.PaletteCategories, c => Assert.True(c.IsVisible));
            Assert.All(viewModel.PaletteCategories.SelectMany(c => c.Nodes), n => Assert.True(n.IsVisible));
        }
    }
}

using System.Linq;
using ModbusForge.Models;
using ModbusForge.ViewModels;
using Xunit;

namespace ModbusForge.Tests
{
    public class VisualNodeMappingTests
    {
        [Fact]
        public void ConvertToSimulationElements_CorrectlyMapsConnections()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();

            var sourceNode = new VisualNode
            {
                Id = "source",
                ElementType = PlcElementType.Input,
                OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 10, Not = true }
            };

            var targetNode = new VisualNode
            {
                Id = "target",
                ElementType = PlcElementType.AND
            };

            viewModel.Nodes.Add(sourceNode);
            viewModel.Nodes.Add(targetNode);

            viewModel.CreateConnection("source", "target", "Input1");

            // Act
            var elements = viewModel.ConvertToSimulationElements();

            // Assert
            var targetElement = elements.First(e => e.Id == "target");
            Assert.Equal(PlcArea.Coil, targetElement.Input1.Area);
            Assert.Equal(10, targetElement.Input1.Address);
            Assert.True(targetElement.Input1.Not);
        }

        [Fact]
        public void ConvertToSimulationElements_HandlesMultipleInputs()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();

            var source1 = new VisualNode
            {
                Id = "s1",
                OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 1 }
            };
            var source2 = new VisualNode
            {
                Id = "s2",
                OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = 2 }
            };
            var target = new VisualNode
            {
                Id = "target",
                ElementType = PlcElementType.AND
            };

            viewModel.Nodes.Add(source1);
            viewModel.Nodes.Add(source2);
            viewModel.Nodes.Add(target);

            viewModel.CreateConnection("s1", "target", "Input1");
            viewModel.CreateConnection("s2", "target", "Input2");

            // Act
            var elements = viewModel.ConvertToSimulationElements();

            // Assert
            var targetElement = elements.First(e => e.Id == "target");
            Assert.Equal(1, targetElement.Input1.Address);
            Assert.Equal(2, targetElement.Input2.Address);
        }
    }
}

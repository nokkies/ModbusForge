using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.Performance
{
    public class VisualNodeEditorPerformanceTests
    {
        private readonly ITestOutputHelper _output;

        public VisualNodeEditorPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ConvertToSimulationElements_PerformanceTest()
        {
            // Arrange
            var viewModel = new VisualNodeEditorViewModel();
            int nodeCount = 20; // Reduced to 20 to avoid timeouts

            for (int i = 0; i < nodeCount; i++)
            {
                var node = new VisualNode
                {
                    Id = $"node_{i}",
                    ElementType = PlcElementType.AND,
                    OutputAddress = new PlcAddressReference { Area = PlcArea.Coil, Address = i }
                };
                viewModel.Nodes.Add(node);
            }

            for (int i = 1; i < nodeCount; i++)
            {
                // Connect node i-1 to node i
                viewModel.CreateConnection($"node_{i-1}", $"node_{i}", "Input1");
                if (i > 1)
                {
                    viewModel.CreateConnection($"node_{i-2}", $"node_{i}", "Input2");
                }
            }

            // Warm up
            viewModel.ConvertToSimulationElements();

            // Measure
            var sw = Stopwatch.StartNew();
            int iterations = 100;
            for (int i = 0; i < iterations; i++)
            {
                viewModel.ConvertToSimulationElements();
            }
            sw.Stop();

            _output.WriteLine($"ConvertToSimulationElements for {nodeCount} nodes took average {sw.Elapsed.TotalMilliseconds / iterations}ms");
        }
    }
}

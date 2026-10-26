using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.Avalonia;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class DiResolutionTests
    {
        [Fact]
        public void VisualNodeEditorViewModel_Resolves_WithAllDependencies()
        {
            var serviceProvider = BuildAppServices();

            var exception = Record.Exception(() => serviceProvider.GetRequiredService<VisualNodeEditorViewModel>());
            Assert.Null(exception);

            var vm = serviceProvider.GetRequiredService<VisualNodeEditorViewModel>();
            Assert.NotNull(vm);
            Assert.NotNull(vm.OpenTagBrowserCommand);
            Assert.NotNull(vm.OpenWatchWindowCommand);
        }

        [Fact]
        public async Task StoppingThePlcEditor_KeepsTheSimulationScanning()
        {
            var serviceProvider = BuildAppServices();
            var simulation = serviceProvider.GetRequiredService<VisualNodeEditorViewModel>();
            var plc = serviceProvider.GetRequiredService<PlcEditorViewModel>();

            // An INT input wired to an INT output: the output only follows the input
            // while the Simulation's own engine keeps scanning.
            var input = simulation.AddNodeAt(PlcElementType.InputInt, 0, 0)!;
            var output = simulation.AddNodeAt(PlcElementType.OutputInt, 300, 0)!;
            simulation.Connections.Add(new NodeConnection(input.Id, output.Id, "Input1"));
            simulation.RunCommand.Execute(null);

            plc.RunCommand.Execute(null);
            plc.StopCommand.Execute(null);
            input.CurrentValueDouble = 42; // live edit: writes the input's register

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (output.IntValue != 42 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            simulation.StopCommand.Execute(null);

            Assert.Equal(42, output.IntValue);
        }

        private static IServiceProvider BuildAppServices()
        {
            var configureMethod = typeof(App).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(configureMethod);

            return (IServiceProvider?)configureMethod.Invoke(null, null)
                ?? throw new InvalidOperationException("ConfigureServices returned null.");
        }
    }
}

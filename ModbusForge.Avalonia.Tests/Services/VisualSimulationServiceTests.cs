using System.Collections.ObjectModel;
using System.Reflection;
using ModbusForge.Data;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests.Services
{
    public class VisualSimulationServiceTests
    {
        [Fact]
        public void WriteNodeValue_InputIntAddressOne_WritesFirstHoldingRegister()
        {
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "input1",
                        Name = "Input1",
                        ElementType = PlcElementType.InputInt,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 1
                        }
                    }
                }
            };

            service.Start(config);

            var ex = Record.Exception(() => service.WriteNodeValue("input1", 42));

            Assert.Null(ex);
            var dataStore = GetDataStore(service);
            Assert.NotNull(dataStore);
            Assert.Equal((ushort)42, dataStore!.HoldingRegisters[1]);
        }

        [Fact]
        public void WriteNodeValue_MathAddOutputAddressTwo_WritesSecondHoldingRegister()
        {
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "add1",
                        Name = "Add",
                        ElementType = PlcElementType.MATH_ADD,
                        OutputAddress = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 2
                        },
                        Input2Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = -1
                        }
                    }
                }
            };

            service.Start(config);

            var ex = Record.Exception(() => service.WriteNodeValue("add1", 123));

            Assert.Null(ex);
            var dataStore = GetDataStore(service);
            Assert.NotNull(dataStore);
            Assert.Equal((ushort)123, dataStore!.HoldingRegisters[2]);
        }

        [Fact]
        public void WriteNodeValue_ZeroAddress_IsIgnored()
        {
            var service = new AvaloniaVisualSimulationService();
            var config = new VisualNodeEditorConfig
            {
                Nodes = new ObservableCollection<VisualNode>
                {
                    new()
                    {
                        Id = "input1",
                        Name = "Input1",
                        ElementType = PlcElementType.InputInt,
                        Input1Address = new PlcAddressReference
                        {
                            Area = PlcArea.HoldingRegister,
                            Address = 0
                        }
                    }
                }
            };

            service.Start(config);

            var ex = Record.Exception(() => service.WriteNodeValue("input1", 42));

            Assert.Null(ex);
        }

        private static DataStore? GetDataStore(AvaloniaVisualSimulationService service)
        {
            var baseType = typeof(AvaloniaVisualSimulationService).BaseType;
            var field = baseType?.GetField("_dataStore", BindingFlags.NonPublic | BindingFlags.Instance);

            return field?.GetValue(service) as DataStore;
        }
    }
}

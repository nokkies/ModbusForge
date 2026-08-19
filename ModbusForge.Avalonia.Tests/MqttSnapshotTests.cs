using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public class MqttSnapshotTests
    {
        [Fact]
        public void Snapshot_IncludesLoadedRegisterRows_AndSkipsReadErrors()
        {
            var vm = CreateVm();

            vm.HoldingRegisters.Add(new RegisterEntry { Address = 1, Value = 50 });
            vm.HoldingRegisters.Add(new RegisterEntry { Address = 2, Value = 70, IsReadError = true });
            vm.InputRegisters.Add(new RegisterEntry { Address = 3, Value = 9 });
            vm.Coils.Add(new CoilEntry { Address = 1, State = true });
            vm.DiscreteInputs.Add(new CoilEntry { Address = 2, State = false });
            vm.DiscreteInputs.Add(new CoilEntry { Address = 3, State = true, IsReadError = true });

            var snapshot = vm.BuildMqttSnapshot().ToList();

            var hr1 = snapshot.Single(u => u.Area == PlcArea.HoldingRegister && u.Address == 1);
            Assert.Equal("HR_1", hr1.TagName);
            Assert.Equal((ushort)50, hr1.Value);

            // The row with a read error is stale - it must not be published.
            Assert.DoesNotContain(snapshot, u => u.Area == PlcArea.HoldingRegister && u.Address == 2);

            var ir3 = snapshot.Single(u => u.Area == PlcArea.InputRegister && u.Address == 3);
            Assert.Equal("IR_3", ir3.TagName);
            Assert.Equal((ushort)9, ir3.Value);

            var coil = snapshot.Single(u => u.Area == PlcArea.Coil && u.Address == 1);
            Assert.Equal("COIL_1", coil.TagName);
            Assert.Equal(true, coil.Value);

            var discrete = snapshot.Single(u => u.Area == PlcArea.DiscreteInput && u.Address == 2);
            Assert.Equal("DI_2", discrete.TagName);
            Assert.Equal(false, discrete.Value);
            Assert.DoesNotContain(snapshot, u => u.Area == PlcArea.DiscreteInput && u.Address == 3);
        }

        [Fact]
        public void Snapshot_CustomEntryWinsOverSameAddress_RegisterRow()
        {
            var vm = CreateVm();

            vm.CustomEntries.Add(new CustomEntry
            {
                Name = "Temp",
                Address = 1,
                Area = "HoldingRegister",
                Type = "real",
                Value = "21.5",
            });
            vm.HoldingRegisters.Add(new RegisterEntry { Address = 1, Value = 50 });

            var snapshot = vm.BuildMqttSnapshot().ToList();

            // Exactly one tag for that area + address, and it is the named custom tag.
            var matches = snapshot.Where(u => u.Area == PlcArea.HoldingRegister && u.Address == 1).ToList();
            var single = Assert.Single(matches);
            Assert.Equal("Temp", single.TagName);
            Assert.Equal(21.5f, Assert.IsType<float>(single.Value));
        }

        [Fact]
        public void Snapshot_CustomValuesPublishedInDeclaredType()
        {
            var vm = CreateVm();

            vm.CustomEntries.Add(new CustomEntry { Name = "Count", Address = 10, Type = "int", Value = "42" });
            vm.CustomEntries.Add(new CustomEntry { Name = "Raw", Address = 11, Type = "uint", Value = "60000" });
            vm.CustomEntries.Add(new CustomEntry { Name = "Speed", Address = 12, Type = "real", Value = "7.25" });
            vm.CustomEntries.Add(new CustomEntry { Name = "Notes", Address = 13, Type = "int", Value = "not a number" });

            var snapshot = vm.BuildMqttSnapshot().ToList();

            Assert.Equal((short)42, snapshot.Single(u => u.TagName == "Count").Value);
            Assert.Equal((ushort)60000, snapshot.Single(u => u.TagName == "Raw").Value);
            Assert.Equal(7.25f, snapshot.Single(u => u.TagName == "Speed").Value);

            // An unparseable value falls back to the raw text rather than dropping the tag.
            Assert.Equal("not a number", snapshot.Single(u => u.TagName == "Notes").Value);
        }

        [Fact]
        public void Snapshot_EmptyWorkspace_PublishesNothing()
        {
            var vm = CreateVm();

            Assert.Empty(vm.BuildMqttSnapshot());
        }

        private static MainViewModel CreateVm()
            => new(
                new ConnectionManager(
                    NullLogger<ConnectionManager>.Instance,
                    NullLoggerFactory.Instance,
                    null),
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher());
    }
}

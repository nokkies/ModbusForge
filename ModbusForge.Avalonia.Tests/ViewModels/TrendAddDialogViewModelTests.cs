using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class TrendAddDialogViewModelTests
    {
        [Fact]
        public void AutoFill_SuggestsAreaAddressName_AndFollowsChanges()
        {
            var vm = new TrendAddDialogViewModel();

            Assert.Equal("HR Trend 1", vm.Name);

            vm.Address = 25;
            Assert.Equal("HR Trend 25", vm.Name);

            vm.SelectedArea = "InputRegister";
            Assert.Equal("IR Trend 25", vm.Name);
        }

        [Fact]
        public void AutoFill_StopsAfterTheUserEditsTheName()
        {
            var vm = new TrendAddDialogViewModel();
            vm.Name = "My pen";

            vm.Address = 25;

            Assert.Equal("My pen", vm.Name);
        }

        [Fact]
        public void Ok_ProducesARegisterResult_WithCurrentValues()
        {
            var vm = new TrendAddDialogViewModel();
            var close = new TrendAddDialogResultEventArgs(null);
            vm.RequestClose += (_, e) => close = (TrendAddDialogResultEventArgs)e;
            vm.Address = 10;
            vm.SelectedArea = "Coil";
            vm.ReadPeriodMs = 250;

            Assert.True(vm.OkCommand.CanExecute(null));
            vm.OkCommand.Execute(null);

            var result = close.Result;
            Assert.NotNull(result);
            Assert.Equal("Coil", result!.Area);
            Assert.Equal(10, result.Address);
            Assert.Equal("Coil Trend 10", result.Name);
            Assert.Equal(250, result.ReadPeriodMs);
        }

        [Fact]
        public void Ok_Disabled_WhenNameIsEmpty()
        {
            var vm = new TrendAddDialogViewModel();
            vm.Name = "  ";

            Assert.False(vm.OkCommand.CanExecute(null));
        }

        [Fact]
        public void TagSource_UsesTheTagAreaAddress_AndSuggestsTheTagName()
        {
            var service = new TagService();
            var tag = new Tag
            {
                Name = "MotorSpeed",
                Group = "Default",
                Area = PlcArea.InputRegister,
                Address = 42
            };
            service.Tags.Add(tag);

            var vm = new TrendAddDialogViewModel(service);
            Assert.Single(vm.TrendableTags);
            Assert.Equal("MotorSpeed", vm.SelectedTag!.Name);

            vm.IsTagSource = true;
            Assert.Equal("MotorSpeed", vm.Name);

            var close = new TrendAddDialogResultEventArgs(null);
            vm.RequestClose += (_, e) => close = (TrendAddDialogResultEventArgs)e;
            vm.OkCommand.Execute(null);

            var result = close.Result;
            Assert.NotNull(result);
            Assert.Equal("InputRegister", result!.Area);
            Assert.Equal(42, result.Address);
            Assert.Equal("MotorSpeed", result.Name);
        }

        [Fact]
        public void BitPackedTags_AreNotOfferedAsPens()
        {
            var service = new TagService();
            service.Tags.Add(new Tag { Name = "BitOnly", Group = "Default", Bit = 3 });
            service.Tags.Add(new Tag { Name = "Whole", Group = "Default" });

            var vm = new TrendAddDialogViewModel(service);

            var tag = Assert.Single(vm.TrendableTags);
            Assert.Equal("Whole", tag.Name);
        }

        [Fact]
        public void Cancel_ResolvesNull()
        {
            var vm = new TrendAddDialogViewModel();
            var close = new TrendAddDialogResultEventArgs(null);
            vm.RequestClose += (_, e) => close = (TrendAddDialogResultEventArgs)e;

            vm.CancelCommand.Execute(null);

            Assert.NotNull(close);
            Assert.Null(close.Result);
        }
    }
}

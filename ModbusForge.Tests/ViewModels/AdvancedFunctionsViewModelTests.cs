using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using Moq;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class AdvancedFunctionsViewModelTests
    {
        private static AdvancedFunctionsViewModel CreateViewModel(Mock<IModbusService> service)
        {
            service.SetupGet(s => s.IsConnected).Returns(true);
            return new AdvancedFunctionsViewModel(service.Object, 1, Mock.Of<ILogger<AdvancedFunctionsViewModel>>());
        }

        [Fact]
        public async Task MaskWrite_CallsServiceAndReportsResult()
        {
            var service = new Mock<IModbusService>();
            service.Setup(s => s.MaskWriteRegisterAsync(1, 5, (ushort)0x00F2, (ushort)0x0025))
                   .ReturnsAsync((ushort)0x0017);
            var vm = CreateViewModel(service);
            vm.MaskWriteAddress = 5;
            vm.AndMask = 0x00F2;
            vm.OrMask = 0x0025;

            await vm.MaskWriteAsync();

            service.Verify(s => s.MaskWriteRegisterAsync(1, 5, (ushort)0x00F2, (ushort)0x0025), Times.Once);
            Assert.Contains("FC22 OK", vm.Status);
        }

        [Fact]
        public async Task MaskWrite_RejectsAddressBelowOne()
        {
            var service = new Mock<IModbusService>();
            var vm = CreateViewModel(service);
            vm.MaskWriteAddress = 0;

            await vm.MaskWriteAsync();

            service.Verify(s => s.MaskWriteRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>()), Times.Never);
            Assert.Contains("1 or greater", vm.Status);
        }

        [Fact]
        public async Task ReadWriteMultiple_ParsesDecimalAndHexValues()
        {
            var service = new Mock<IModbusService>();
            service.Setup(s => s.ReadWriteMultipleRegistersAsync(1, 10, 2, 20, It.IsAny<ushort[]>()))
                   .ReturnsAsync(new ushort[] { 7, 8 });
            var vm = CreateViewModel(service);
            vm.ReadAddress = 10;
            vm.ReadCount = 2;
            vm.WriteAddress = 20;
            vm.WriteValues = "1, 0x0A 3";

            await vm.ReadWriteMultipleAsync();

            service.Verify(s => s.ReadWriteMultipleRegistersAsync(
                1, 10, 2, 20, It.Is<ushort[]>(v => v.Length == 3 && v[0] == 1 && v[1] == 10 && v[2] == 3)), Times.Once);
            Assert.Contains("FC23 OK", vm.Status);
        }

        [Fact]
        public async Task ReadWriteMultiple_ReportsInvalidValues()
        {
            var service = new Mock<IModbusService>();
            var vm = CreateViewModel(service);
            vm.WriteValues = "1, banana";

            await vm.ReadWriteMultipleAsync();

            service.Verify(s => s.ReadWriteMultipleRegistersAsync(
                It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ushort[]>()), Times.Never);
            Assert.Contains("banana", vm.Status);
        }

        [Fact]
        public async Task ReadDeviceIdentification_PopulatesItems()
        {
            var identification = DeviceIdentification.CreateDefault("1.2.3");
            var service = new Mock<IModbusService>();
            service.Setup(s => s.ReadDeviceIdentificationAsync(1, DeviceIdObject.VendorName, DeviceIdCategory.Basic))
                   .ReturnsAsync(identification);
            var vm = CreateViewModel(service);

            await vm.ReadDeviceIdentificationAsync();

            Assert.Equal(identification.Objects.Count, vm.DeviceIdentificationItems.Count);
            Assert.Contains(vm.DeviceIdentificationItems, i => i.Value == "ModbusForge");
            Assert.Contains("FC43 OK", vm.Status);
        }

        [Fact]
        public async Task Operations_ReportNotConnected()
        {
            var service = new Mock<IModbusService>();
            service.SetupGet(s => s.IsConnected).Returns(false);
            var vm = new AdvancedFunctionsViewModel(service.Object, 1, Mock.Of<ILogger<AdvancedFunctionsViewModel>>());

            await vm.ReadDeviceIdentificationAsync();

            Assert.Equal("Not connected.", vm.Status);
        }
    }
}

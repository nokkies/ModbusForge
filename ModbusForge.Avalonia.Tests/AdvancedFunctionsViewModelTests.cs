using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class AdvancedFunctionsViewModelTests
    {
        [Fact]
        public async Task MaskWrite_CallsServiceAndReportsResult()
        {
            var service = new FakeModbusService
            {
                IsConnected = true,
                MaskWriteResult = 0x0017
            };
            var vm = CreateViewModel(service);
            vm.MaskWriteAddress = 5;
            vm.AndMask = 0x00F2;
            vm.OrMask = 0x0025;

            await vm.MaskWriteAsync();

            Assert.Equal((byte)1, service.LastUnitId);
            Assert.Equal(5, service.LastMaskWriteAddress);
            Assert.Equal((ushort)0x00F2, service.LastAndMask);
            Assert.Equal((ushort)0x0025, service.LastOrMask);
            Assert.Contains("FC22 OK", vm.Status);
        }

        [Fact]
        public async Task ReadWriteMultiple_ParsesDecimalAndHexValues()
        {
            var service = new FakeModbusService
            {
                IsConnected = true,
                ReadWriteResult = new ushort[] { 7, 8 }
            };
            var vm = CreateViewModel(service);
            vm.ReadAddress = 10;
            vm.ReadCount = 2;
            vm.WriteAddress = 20;
            vm.WriteValues = "1, 0x0A 3";

            await vm.ReadWriteMultipleAsync();

            Assert.Equal(10, service.LastReadAddress);
            Assert.Equal(2, service.LastReadCount);
            Assert.Equal(20, service.LastWriteAddress);
            Assert.Equal(new ushort[] { 1, 10, 3 }, service.LastWriteValues);
            Assert.Contains("FC23 OK", vm.Status);
        }

        [Fact]
        public async Task InvalidWriteValue_IsReportedWithoutCallingService()
        {
            var service = new FakeModbusService { IsConnected = true };
            var vm = CreateViewModel(service);
            vm.WriteValues = "1, banana";

            await vm.ReadWriteMultipleAsync();

            Assert.Contains("banana", vm.Status);
            Assert.Null(service.LastWriteValues);
        }

        [Fact]
        public async Task ReadDeviceIdentification_PopulatesSortedItems()
        {
            var identification = new DeviceIdentification
            {
                ConformityLevel = 0x82,
                VendorName = "ModbusForge",
                ProductCode = "MF-TCP",
                MajorMinorRevision = "1.2.3"
            };
            var service = new FakeModbusService
            {
                IsConnected = true,
                Identification = identification
            };
            var vm = CreateViewModel(service);

            await vm.ReadDeviceIdentificationAsync();

            Assert.Equal(3, vm.DeviceIdentificationItems.Count);
            Assert.Equal(new byte[] { 0, 1, 2 }, vm.DeviceIdentificationItems.Select(item => item.ObjectId));
            Assert.Contains(vm.DeviceIdentificationItems, item => item.Name == "Vendor name" && item.Value == "ModbusForge");
            Assert.Contains("FC43 OK", vm.Status);
        }

        [Fact]
        public async Task InvalidAddress_IsReportedWithoutCallingService()
        {
            var service = new FakeModbusService { IsConnected = true };
            var vm = CreateViewModel(service);
            vm.MaskWriteAddress = 0;

            await vm.MaskWriteAsync();

            Assert.Contains("1 or greater", vm.Status);
            Assert.Null(service.LastMaskWriteAddress);
        }

        private static AdvancedFunctionsViewModel CreateViewModel(FakeModbusService service) =>
            new(service, 1, NullLogger<AdvancedFunctionsViewModel>.Instance);

        private sealed class FakeModbusService : IModbusService
        {
            public bool IsConnected { get; set; }
            public string BoundEndpoint => string.Empty;
            public ModbusFrameLogger FrameLogger { get; } = new();
            public ushort? MaskWriteResult { get; set; }
            public ushort[]? ReadWriteResult { get; set; }
            public DeviceIdentification? Identification { get; set; }
            public byte? LastUnitId { get; private set; }
            public int? LastMaskWriteAddress { get; private set; }
            public ushort LastAndMask { get; private set; }
            public ushort LastOrMask { get; private set; }
            public int LastReadAddress { get; private set; }
            public int LastReadCount { get; private set; }
            public int LastWriteAddress { get; private set; }
            public ushort[]? LastWriteValues { get; private set; }

            public Task<bool> ConnectAsync(
                string ipAddress,
                int port,
                string unitIds = "1",
                CancellationToken cancellationToken = default) => Task.FromResult(true);

            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<ConnectionDiagnosticResult> RunDiagnosticsAsync(string ipAddress, int port, byte unitId) =>
                Task.FromResult(new ConnectionDiagnosticResult());

            public Task<ushort[]?> ReadHoldingRegistersAsync(byte unitId, int startAddress, int count) =>
                Task.FromResult<ushort[]?>(null);

            public Task<ushort[]?> ReadInputRegistersAsync(byte unitId, int startAddress, int count) =>
                Task.FromResult<ushort[]?>(null);

            public Task WriteSingleRegisterAsync(byte unitId, int registerAddress, ushort value) =>
                Task.CompletedTask;

            public Task WriteRegistersAsync(byte unitId, int startAddress, ushort[] values) =>
                Task.CompletedTask;

            public Task<bool[]?> ReadCoilsAsync(byte unitId, int startAddress, int count) =>
                Task.FromResult<bool[]?>(null);

            public Task<bool[]?> ReadDiscreteInputsAsync(byte unitId, int startAddress, int count) =>
                Task.FromResult<bool[]?>(null);

            public Task WriteSingleCoilAsync(byte unitId, int coilAddress, bool value) =>
                Task.CompletedTask;

            public Task<ushort?> MaskWriteRegisterAsync(byte unitId, int registerAddress, ushort andMask, ushort orMask)
            {
                LastUnitId = unitId;
                LastMaskWriteAddress = registerAddress;
                LastAndMask = andMask;
                LastOrMask = orMask;
                return Task.FromResult(MaskWriteResult);
            }

            public Task<ushort[]?> ReadWriteMultipleRegistersAsync(
                byte unitId,
                int readStartAddress,
                int readCount,
                int writeStartAddress,
                ushort[] writeValues)
            {
                LastUnitId = unitId;
                LastReadAddress = readStartAddress;
                LastReadCount = readCount;
                LastWriteAddress = writeStartAddress;
                LastWriteValues = writeValues;
                return Task.FromResult(ReadWriteResult);
            }

            public Task<DeviceIdentification?> ReadDeviceIdentificationAsync(
                byte unitId,
                byte objectId = DeviceIdObject.VendorName,
                DeviceIdCategory category = DeviceIdCategory.Basic)
            {
                LastUnitId = unitId;
                return Task.FromResult(Identification);
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public sealed class MainViewModelIntegrationTests
    {
        [Fact]
        public async Task Connect_And_Poll_Populates_Holding_Registers()
        {
            // Arrange: start a local multi-unit Modbus TCP server on a free port.
            using var server = CreateTestServer();
            var port = ((IPEndPoint?)server.LocalEndpoint)?.Port
                ?? throw new InvalidOperationException("Server not bound");

            var modbus = new ModbusTcpService(NullLogger<ModbusTcpService>.Instance);
            var dispatcher = new SyncDispatcher();
            var vm = new MainViewModel(modbus, NullLogger<MainViewModel>.Instance, dispatcher);

            vm.Host = "127.0.0.1";
            vm.Port = port;
            vm.UnitId = 1;
            vm.StartAddress = 0;
            vm.RegisterCount = 5;

            // Act: connect and wait for one poll cycle.
            await vm.ConnectCommand.ExecuteAsync(null);
            await Task.Delay(TimeSpan.FromSeconds(1.5));

            // Assert
            Assert.True(vm.IsConnected);
            Assert.Equal(5, vm.Registers.Count);
            Assert.Equal("10", vm.Registers[0].ValueText);
            Assert.Equal("20", vm.Registers[1].ValueText);
            Assert.Equal("30", vm.Registers[2].ValueText);
            Assert.Equal("40", vm.Registers[3].ValueText);
            Assert.Equal("50", vm.Registers[4].ValueText);

            // Cleanup
            await vm.DisconnectCommand.ExecuteAsync(null);
            vm.Dispose();
            modbus.Dispose();
            server.Stop();
        }

        private static ModbusMultiUnitServer CreateTestServer()
        {
            var logger = NullLogger.Instance;
            var server = new ModbusMultiUnitServer(logger);
            server.Start(new IPEndPoint(IPAddress.Loopback, 0), new[] { (byte)1 });
            return server;
        }
    }
}

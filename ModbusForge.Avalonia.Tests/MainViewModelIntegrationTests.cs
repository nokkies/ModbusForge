using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
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

            var loggerFactory = NullLoggerFactory.Instance;

            // Private profile store - the manager persists on every change.
            var profileDir = Path.Combine(Path.GetTempPath(), "ModbusForgeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profileDir);

            var connectionManager = new ConnectionManager(
                NullLogger<ConnectionManager>.Instance,
                loggerFactory,
                validationService: null,
                correlationContext: null,
                addressValidator: null,
                profilesFilePath: Path.Combine(profileDir, "connection-profiles.json"));

            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1) { Mode = "Client" };
            connectionManager.AddProfile(profile);
            connectionManager.SetActiveProfile(profile);

            var dispatcher = new SyncDispatcher();
            var vm = new MainViewModel(connectionManager, NullLogger<MainViewModel>.Instance, dispatcher);

            // Per-area configuration (the selected area defaults to HoldingRegister).
            vm.HoldingRegisterStart = 0;
            vm.HoldingRegisterCount = 5;

            // Act: connect, then wait for the first poll cycle to land (event-style
            // polling with a deadline instead of a fixed 1.5 s sleep).
            await vm.ConnectCommand.ExecuteAsync(null);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (vm.Registers.Count == 0 ||
                   string.IsNullOrEmpty(vm.Registers[0].ValueText) ||
                   vm.Registers.Count < 5)
            {
                if (DateTime.UtcNow > deadline) break;
                await Task.Delay(50);
            }

            // Assert
            Assert.NotNull(vm.ActiveProfile);
            Assert.True(vm.ActiveProfile.IsConnected);
            Assert.Equal(5, vm.Registers.Count);
            Assert.Equal("10", vm.Registers[0].ValueText);
            Assert.Equal("20", vm.Registers[1].ValueText);
            Assert.Equal("30", vm.Registers[2].ValueText);
            Assert.Equal("40", vm.Registers[3].ValueText);
            Assert.Equal("50", vm.Registers[4].ValueText);

            // Cleanup
            await vm.DisconnectCommand.ExecuteAsync(null);
            vm.Dispose();
            server.Stop();

            try { Directory.Delete(profileDir, recursive: true); }
            catch (IOException) { /* best effort */ }
        }

        [Fact]
        public async Task Poll_UpdatesSymbolicTagsForTheReadRange()
        {
            using var server = CreateTestServer();
            var port = ((IPEndPoint?)server.LocalEndpoint)?.Port
                ?? throw new InvalidOperationException("Server not bound");

            var connectionManager = new ConnectionManager(
                NullLogger<ConnectionManager>.Instance,
                NullLoggerFactory.Instance,
                null);
            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1) { Mode = "Client" };
            connectionManager.AddProfile(profile);
            connectionManager.SetActiveProfile(profile);

            var tagService = new TagService();
            // The area start clamps to 1, so the monitored range is 1..5; the
            // built-in server seeds address 1 with 10 (i*10 for register i).
            var tag = new Tag
            {
                Name = "HR1",
                Area = PlcArea.HoldingRegister,
                Address = 1,
                DataType = TagDataType.UInt16,
            };
            tagService.Tags.Add(tag);

            var vm = new MainViewModel(
                connectionManager,
                NullLogger<MainViewModel>.Instance,
                new SyncDispatcher(),
                tagService: tagService);

            vm.StartAddress = 1;
            vm.RegisterCount = 5;

            await vm.ConnectCommand.ExecuteAsync(null);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (tag.CurrentValue is not ushort && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100);
                }

                Assert.True(tag.CurrentValue is not null,
                    $"tag not refreshed: grid count={vm.Registers.Count}, status='{vm.StatusMessage}', " +
                    $"connected={vm.ActiveProfile?.IsConnected}");
                Assert.Equal((ushort)10, Assert.IsType<ushort>(tag.CurrentValue));
                Assert.True(tag.LastUpdated > DateTime.MinValue);
            }
            finally
            {
                await vm.DisconnectCommand.ExecuteAsync(null);
                vm.Dispose();
                server.Stop();
            }
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

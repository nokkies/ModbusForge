using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests
{
    public class ServerMirrorTests
    {
        [Fact]
        public void ConnectionProfile_Clone_CopiesServerMirrorSettings()
        {
            var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1)
            {
                EnableServerMirror = true,
                ServerMirrorPort = 5025
            };

            var clone = profile.Clone();

            Assert.True(clone.EnableServerMirror);
            Assert.Equal(5025, clone.ServerMirrorPort);
        }

        [Fact]
        public void ConnectionManager_SaveAndLoad_PreservesServerMirrorSettings()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var mgr1 = new ConnectionManager(
                    NullLogger<ConnectionManager>.Instance,
                    NullLoggerFactory.Instance,
                    profilesFilePath: tempFile);

                var profile = new ConnectionProfile("MirrorTest", "127.0.0.1", 502, 1)
                {
                    Mode = "Client",
                    EnableServerMirror = true,
                    ServerMirrorPort = 5090
                };
                mgr1.AddProfile(profile);
                mgr1.SaveProfiles();

                var mgr2 = new ConnectionManager(
                    NullLogger<ConnectionManager>.Instance,
                    NullLoggerFactory.Instance,
                    profilesFilePath: tempFile);

                var loaded = Assert.Single(mgr2.Profiles, p => p.Name == "MirrorTest");
                Assert.True(loaded.EnableServerMirror);
                Assert.Equal(5090, loaded.ServerMirrorPort);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ConnectionManager_MirrorData_UpdatesDataStore()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var mgr = new ConnectionManager(
                    NullLogger<ConnectionManager>.Instance,
                    NullLoggerFactory.Instance,
                    profilesFilePath: tempFile);

                var profile = new ConnectionProfile("ClientProfile", "127.0.0.1", 502, 1)
                {
                    Mode = "Client",
                    EnableServerMirror = true,
                    ServerMirrorPort = 51234
                };
                mgr.AddProfile(profile);
                mgr.SetActiveProfile(profile);

                // Spin up a fake Modbus server to connect our client to
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int fakePort = ((IPEndPoint)listener.LocalEndpoint).Port;
                profile.Port = fakePort;

                var acceptTask = listener.AcceptTcpClientAsync();
                var connectTask = mgr.ConnectProfileAsync(profile);

                using (var client = await acceptTask)
                {
                    bool connected = await connectTask;
                    Assert.True(connected);

                    var mirrorServer = mgr.GetMirrorServerForProfile(profile);
                    Assert.NotNull(mirrorServer);

                    // Mirror holding registers
                    ushort[] hrValues = new ushort[] { 100, 200, 300 };
                    mgr.MirrorHoldingRegisters(1, 10, hrValues);

                    var ds = mirrorServer.GetDataStore(1) ?? mirrorServer.GetDataStore();
                    Assert.NotNull(ds);
                    Assert.Equal((ushort)100, ds.HoldingRegisters[10]);
                    Assert.Equal((ushort)200, ds.HoldingRegisters[11]);
                    Assert.Equal((ushort)300, ds.HoldingRegisters[12]);

                    // Mirror coils
                    bool[] coilValues = new bool[] { true, false, true };
                    mgr.MirrorCoils(1, 5, coilValues);
                    Assert.True(ds.CoilDiscretes[5]);
                    Assert.False(ds.CoilDiscretes[6]);
                    Assert.True(ds.CoilDiscretes[7]);

                    await mgr.DisconnectProfileAsync(profile);
                    Assert.Null(mgr.GetMirrorServerForProfile(profile));
                }

                listener.Stop();
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
    }
}

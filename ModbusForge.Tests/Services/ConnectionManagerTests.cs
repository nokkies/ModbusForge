using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

[Collection("ConnectionManagerTests")]
public class ConnectionManagerTests : IDisposable
{
    private readonly Mock<ILogger<ConnectionManager>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly ConnectionManager _manager;

    private readonly bool _backupExisted;

    public ConnectionManagerTests()
    {
        _mockLogger = new Mock<ILogger<ConnectionManager>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();

        // Mock the creation of ModbusTcpService logger inside GetOrCreateService
        _mockLoggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        // Reset the default profiles file by moving it out of the way or replacing it if it exists.
        // It's saved in ApplicationData.
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModbusForge",
            "connection-profiles.json");

        if (File.Exists(profilePath))
        {
            _backupExisted = true;
            File.Move(profilePath, profilePath + ".bak", true);
        }
        else
        {
            _backupExisted = false;
        }

        _manager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);
        // Start fresh for each test by removing the default profile
        while (_manager.Profiles.Count > 0)
        {
            _manager.RemoveProfile(_manager.Profiles[0]);
        }
    }

    public void Dispose()
    {
        // Restore backup if it exists
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModbusForge",
            "connection-profiles.json");

        if (_backupExisted && File.Exists(profilePath + ".bak"))
        {
            File.Move(profilePath + ".bak", profilePath, true);
        }
        else if (!_backupExisted && File.Exists(profilePath))
        {
            // If there was no backup, it means the test created a new file. Clean it up.
            File.Delete(profilePath);
        }
    }

    [Fact]
    public async Task ConnectProfileAsync_WhenInvalidHost_SetsStatusAndReturnsFalse()
    {
        // Arrange
        // Use an invalid host name
        var profile = new ConnectionProfile("Test", "invalid.local", 502, 1);
        _manager.AddProfile(profile);
        bool eventFired = false;
        _manager.ProfileConnected += (s, p) => eventFired = true;

        // Act
        var result = await _manager.ConnectProfileAsync(profile);

        // Assert
        Assert.False(result);
        Assert.False(profile.IsConnected);
        Assert.Contains("Failed", profile.Status);
        Assert.False(eventFired);
    }

    [Fact]
    public void Constructor_WhenNoProfilesExist_AddsDefaultProfile()
    {
        // Arrange
        // The mock file is already moved out of the way in the constructor,
        // so a new instance will have no loaded profiles.

        // Act
        var newManager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);

        // Assert
        Assert.Single(newManager.Profiles);
        var defaultProfile = newManager.Profiles.First();
        Assert.Equal("Default", defaultProfile.Name);
        Assert.Equal("127.0.0.1", defaultProfile.IpAddress);
        Assert.Equal(502, defaultProfile.Port);
        Assert.Equal(1, defaultProfile.UnitId);
    }

    [Fact]
    public void LoadProfiles_WhenEnumValuesAreStrings_LoadsProfiles()
    {
        // Regression: a profiles file whose enums are written as strings
        // (e.g. hand-edited) must load; previously the whole file was
        // silently dropped and the code-default profile was used instead.
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModbusForge",
            "connection-profiles.json");

        var json = """
        {
          "ActiveProfileId": "str-enum-test",
          "Profiles": [
            {
              "Id": "str-enum-test",
              "Name": "String Enums",
              "IpAddress": "127.0.0.1",
              "Port": 1502,
              "UnitId": 2,
              "Mode": "Client",
              "ServerUnitIds": "1",
              "Transport": "Tcp",
              "ComPort": "COM3",
              "BaudRate": 19200,
              "Parity": "Even",
              "DataBits": 7,
              "StopBits": "Two",
              "RtsEnable": false
            }
          ]
        }
        """;
        File.WriteAllText(profilePath, json);

        try
        {
            // Act: a fresh manager loads the file created above.
            var manager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);

            // Assert
            var profile = Assert.Single(manager.Profiles);
            Assert.Equal("String Enums", profile.Name);
            Assert.Equal(1502, profile.Port);
            Assert.Equal("Client", profile.Mode);
            Assert.Equal(TransportType.Tcp, profile.Transport);
            Assert.Equal(Parity.Even, profile.Parity);
            Assert.Equal(StopBits.Two, profile.StopBits);
            Assert.Same(profile, manager.ActiveProfile);
        }
        finally
        {
            File.Delete(profilePath);
        }
    }

    [Fact]
    public void AddProfile_AddsToListAndSetsActive_IfNoneActive()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1);
        Assert.Empty(_manager.Profiles);
        Assert.Null(_manager.ActiveProfile);

        // Act
        _manager.AddProfile(profile);

        // Assert
        Assert.Single(_manager.Profiles);
        Assert.Contains(profile, _manager.Profiles);
        Assert.Equal(profile, _manager.ActiveProfile);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public void AddProfile_DoesNotSetActive_IfAlreadyActiveExists()
    {
        // Arrange
        var profile1 = new ConnectionProfile("Test 1", "127.0.0.1", 502, 1);
        var profile2 = new ConnectionProfile("Test 2", "127.0.0.1", 502, 1);

        _manager.AddProfile(profile1); // This one becomes active

        // Act
        _manager.AddProfile(profile2);

        // Assert
        Assert.Equal(2, _manager.Profiles.Count);
        Assert.Equal(profile1, _manager.ActiveProfile);
        Assert.True(profile1.IsActive);
        Assert.False(profile2.IsActive);
    }

    [Fact]
    public void RemoveProfile_RemovesFromListAndUpdatesActiveProfile()
    {
        // Arrange
        var profile1 = new ConnectionProfile("Test 1", "127.0.0.1", 502, 1);
        var profile2 = new ConnectionProfile("Test 2", "127.0.0.1", 502, 1);
        _manager.AddProfile(profile1);
        _manager.AddProfile(profile2);

        // Ensure profile1 is active initially
        _manager.SetActiveProfile(profile1);

        // Act
        _manager.RemoveProfile(profile1);

        // Assert
        Assert.Single(_manager.Profiles);
        Assert.DoesNotContain(profile1, _manager.Profiles);
        // It should fallback to the first available profile if active profile is removed
        Assert.Equal(profile2, _manager.ActiveProfile);
        Assert.True(profile2.IsActive);
    }

    [Fact]
    public void RemoveProfile_RemovesFromListAndSetsNullActive_IfLastProfile()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1);
        _manager.AddProfile(profile);

        // Act
        _manager.RemoveProfile(profile);

        // Assert
        Assert.Empty(_manager.Profiles);
        Assert.Null(_manager.ActiveProfile);
    }

    [Fact]
    public void SetActiveProfile_UpdatesActiveProfileAndFiresEvent()
    {
        // Arrange
        var profile1 = new ConnectionProfile("Test 1", "127.0.0.1", 502, 1);
        var profile2 = new ConnectionProfile("Test 2", "127.0.0.1", 502, 1);
        _manager.AddProfile(profile1);
        _manager.AddProfile(profile2);

        ConnectionProfile? receivedProfile = null;
        _manager.ActiveProfileChanged += (sender, p) => receivedProfile = p;

        // Act
        _manager.SetActiveProfile(profile2);

        // Assert
        Assert.Equal(profile2, _manager.ActiveProfile);
        Assert.True(profile2.IsActive);
        Assert.False(profile1.IsActive);
        Assert.Equal(profile2, receivedProfile);
    }

    [Fact]
    public async Task ConnectProfileAsync_WhenFails_SetsStatusAndReturnsFalse()
    {
        // Arrange
        // Use an invalid port that nothing is listening on
        var profile = new ConnectionProfile("Test", "127.0.0.1", 12345, 1);
        _manager.AddProfile(profile);
        bool eventFired = false;
        _manager.ProfileConnected += (s, p) => eventFired = true;

        // Act
        var result = await _manager.ConnectProfileAsync(profile);

        // Assert
        Assert.False(result);
        Assert.False(profile.IsConnected);
        Assert.Contains("Failed", profile.Status);
        Assert.False(eventFired);
    }

    [Fact]
    public async Task ConnectProfileAsync_WhenSuccessful_SetsIsConnectedAndFiresEvent()
    {
        // Arrange
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1);
            _manager.AddProfile(profile);

            ConnectionProfile? connectedProfile = null;
            _manager.ProfileConnected += (s, p) => connectedProfile = p;

            // Accept connection in background
            _ = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
                client.Close();
            });

            // Act
            var result = await _manager.ConnectProfileAsync(profile);

            // Assert
            Assert.True(result);
            Assert.True(profile.IsConnected);
            Assert.Equal("Connected", profile.Status);
            Assert.Equal(profile, connectedProfile);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectProfileAsync_WhenPeerClosesSocket_MarksProfileDisconnected()
    {
        // Arrange
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var profile = new ConnectionProfile("Loss Test", "127.0.0.1", port, 1);
            _manager.AddProfile(profile);

            var disconnectedProfiles = new List<ConnectionProfile>();
            _manager.ProfileDisconnected += (s, p) => disconnectedProfiles.Add(p);

            // Connect (the listener completes the TCP handshake automatically).
            var connected = await _manager.ConnectProfileAsync(profile);
            Assert.True(connected);

            // Retrieve the peer side and close it to simulate the device
            // dropping the connection out from under the client.
            var peer = await Task.Run(() => listener.AcceptTcpClientAsync());
            peer.Close();

            // Act: the service detects the loss on its next request. The in-flight
            // request itself fails because the peer vanished â€” that is expected.
            try
            {
                await _manager.ActiveService!.ReadHoldingRegistersAsync(1, 0, 10);
            }
            catch
            {
                // Expected: the request fails on the dead transport.
            }

            // Assert: the profile is flipped to disconnected and the event fired,
            // even though nobody explicitly disconnected.
            await Task.Delay(250);
            Assert.False(profile.IsConnected);
            Assert.Equal("Connection lost", profile.Status);
            Assert.Contains(profile, disconnectedProfiles);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Connect_Disconnect_Reconnect_LifecycleTest()
    {
        // Arrange
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var profile = new ConnectionProfile("Lifecycle Test", "127.0.0.1", port, 1);
            _manager.AddProfile(profile);

            // Background accept tasks
            _ = Task.Run(async () =>
            {
                var client1 = await listener.AcceptTcpClientAsync();
                var client2 = await listener.AcceptTcpClientAsync();
            });

            // Act 1: Connect
            var result1 = await _manager.ConnectProfileAsync(profile);
            Assert.True(result1);
            Assert.True(profile.IsConnected);
            Assert.Equal("Connected", profile.Status);

            // Act 2: Disconnect
            await _manager.DisconnectProfileAsync(profile);
            Assert.False(profile.IsConnected);
            Assert.Equal("Disconnected", profile.Status);

            // Act 3: Reconnect
            var result3 = await _manager.ConnectProfileAsync(profile);
            Assert.True(result3);
            Assert.True(profile.IsConnected);
            Assert.Equal("Connected", profile.Status);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectProfileAsync_DoubleConnect_ReturnsTrue()
    {
        // Arrange
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1);
            _manager.AddProfile(profile);

            // Accept connections in background
            _ = Task.Run(async () =>
            {
                var client1 = await listener.AcceptTcpClientAsync();
                var client2 = await listener.AcceptTcpClientAsync();
            });

            // Act
            var result1 = await _manager.ConnectProfileAsync(profile);
            var result2 = await _manager.ConnectProfileAsync(profile);

            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.True(profile.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DisconnectProfileAsync_WhenConnected_DisconnectsAndFiresEvent()
    {
        // Arrange
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1);
            _manager.AddProfile(profile);

            _ = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
            });

            await _manager.ConnectProfileAsync(profile);
            Assert.True(profile.IsConnected);

            ConnectionProfile? disconnectedProfile = null;
            _manager.ProfileDisconnected += (s, p) => disconnectedProfile = p;

            // Act
            await _manager.DisconnectProfileAsync(profile);

            // Assert
            Assert.False(profile.IsConnected);
            Assert.Equal("Disconnected", profile.Status);
            Assert.Equal(profile, disconnectedProfile);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DisconnectProfileAsync_WhenDisconnected_DoesNotThrow()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1);
        _manager.AddProfile(profile);
        Assert.False(profile.IsConnected);

        ConnectionProfile? disconnectedProfile = null;
        _manager.ProfileDisconnected += (s, p) => disconnectedProfile = p;

        // Act & Assert
        // Should not throw and should fire disconnected event
        var ex = await Record.ExceptionAsync(() => _manager.DisconnectProfileAsync(profile));
        Assert.Null(ex);
        Assert.False(profile.IsConnected);
        Assert.Equal("Disconnected", profile.Status);
        Assert.Equal(profile, disconnectedProfile);
    }

    [Fact]
    public async Task DisconnectAllAsync_DisconnectsAllConnectedProfiles()
    {
        // Arrange
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var profile1 = new ConnectionProfile("Test 1", "127.0.0.1", port, 1);
            var profile2 = new ConnectionProfile("Test 2", "127.0.0.1", port, 1);
            var profile3 = new ConnectionProfile("Test 3", "127.0.0.1", port, 1);

            _manager.AddProfile(profile1);
            _manager.AddProfile(profile2);
            _manager.AddProfile(profile3); // Keep this one disconnected

            _ = Task.Run(async () =>
            {
                var c1 = await listener.AcceptTcpClientAsync();
                var c2 = await listener.AcceptTcpClientAsync();
            });

            await _manager.ConnectProfileAsync(profile1);
            await _manager.ConnectProfileAsync(profile2);

            Assert.True(profile1.IsConnected);
            Assert.True(profile2.IsConnected);
            Assert.False(profile3.IsConnected);

            int disconnectCount = 0;
            _manager.ProfileDisconnected += (s, p) => disconnectCount++;

            // Act
            await _manager.DisconnectAllAsync();

            // Assert
            Assert.False(profile1.IsConnected);
            Assert.False(profile2.IsConnected);
            Assert.False(profile3.IsConnected);
            Assert.Equal(2, disconnectCount); // Only connected profiles get disconnected
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectionLost_UpdatesProfileStateAndFiresEvent()
    {
        // Arrange: a loopback server that accepts the connection and then
        // resets it, so the next I/O through the service detects the dead socket.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            _ = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
                // Linger 0 -> close() sends an RST instead of a FIN, so the
                // client's next read fails immediately with a reset.
                client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
                client.Close();
            });

            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1);
            _manager.AddProfile(profile);
            Assert.True(await _manager.ConnectProfileAsync(profile));

            ConnectionProfile? disconnectedProfile = null;
            _manager.ProfileDisconnected += (s, p) => disconnectedProfile = p;

            // Act: the next read hits the dead socket. The service swallows the
            // I/O error into a default return and raises ConnectionLost.
            var service = _manager.GetServiceForProfile(profile);
            Assert.NotNull(service);
            await service!.ReadHoldingRegistersAsync(1, 0, 1);

            // Assert
            Assert.False(profile.IsConnected);
            Assert.Equal("Connection lost", profile.Status);
            Assert.Equal(profile, disconnectedProfile);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectionLost_FromWorkerThread_MarshalsStateUpdateToDispatcher()
    {
        // Arrange: a manager whose dispatcher queues work instead of running it.
        // If the connection-loss handling is marshalled (as it must be - the
        // profile list and observable profile state are UI-thread-owned), the
        // profile is still "connected" when the failing read returns, and only
        // becomes disconnected once the queued work runs.
        var dispatcher = new QueuedDispatcher();
        var manager = new ConnectionManager(
            _mockLogger.Object, _mockLoggerFactory.Object, null, null, null, dispatcher);

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            _ = Task.Run(async () =>
            {
                var client = await listener.AcceptTcpClientAsync();
                client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
                client.Close();
            });

            var profile = new ConnectionProfile("Test", "127.0.0.1", port, 1);
            manager.AddProfile(profile);
            Assert.True(await manager.ConnectProfileAsync(profile));

            ConnectionProfile? disconnectedProfile = null;
            manager.ProfileDisconnected += (s, p) => disconnectedProfile = p;

            // Act: the next read hits the dead socket and queues the
            // connection-loss handling on the dispatcher.
            var service = manager.GetServiceForProfile(profile);
            Assert.NotNull(service);
            await service!.ReadHoldingRegistersAsync(1, 0, 1);

            // The handling must not have run inline on the socket worker.
            Assert.True(profile.IsConnected, "profile was updated before the dispatcher ran its queued work");

            // Assert: once the dispatcher runs the queued work, the profile is
            // updated and the event fires.
            await dispatcher.WaitUntilPostedAsync();
            dispatcher.Drain();

            Assert.False(profile.IsConnected);
            Assert.Equal("Connection lost", profile.Status);
            Assert.Equal(profile, disconnectedProfile);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void SaveProfiles_WritesProfilesAndLeavesNoTempFile()
    {
        // Arrange
        _manager.AddProfile(new ConnectionProfile("Custom Profile", "192.168.1.100", 5020, 2));

        // Act
        _manager.SaveProfiles();

        // Assert
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModbusForge",
            "connection-profiles.json");

        Assert.True(File.Exists(profilePath));
        var json = File.ReadAllText(profilePath);
        Assert.Contains("Custom Profile", json);
        Assert.False(File.Exists(profilePath + ".tmp"));
    }

    [Fact]
    public async Task GetServiceForProfile_ReturnsService_IfProfileHasBeenUsed()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 12345, 1);
        _manager.AddProfile(profile);

        // Before connecting, no service exists yet
        Assert.Null(_manager.GetServiceForProfile(profile));

        // Act
        // Attempt a connection (even if it fails, it will create the service)
        await _manager.ConnectProfileAsync(profile);

        // Assert
        var service = _manager.GetServiceForProfile(profile);
        Assert.NotNull(service);
        Assert.IsAssignableFrom<IModbusService>(service);
    }

    [Fact]
    public async Task ActiveService_ReturnsService_IfActiveProfileSet()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 12345, 1);
        _manager.AddProfile(profile);
        // AddProfile auto-sets ActiveProfile if there wasn't one

        // At this point ActiveService is null because service isn't created yet
        Assert.Null(_manager.ActiveService);

        // Act
        await _manager.ConnectProfileAsync(profile);

        // Assert
        Assert.NotNull(_manager.ActiveService);
        Assert.Equal(_manager.GetServiceForProfile(profile), _manager.ActiveService);
    }

    [Fact]
    public void Constructor_WhenProfilesExist_DoesNotAddDefault()
    {
        // Arrange
        // We'll create one manager, add a non-default profile, and save it.
        var initialManager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);
        initialManager.Profiles.Clear();
        initialManager.AddProfile(new ConnectionProfile("Custom Profile", "192.168.1.100", 5020, 2));
        initialManager.SaveProfiles();

        // Act
        // The second manager should load the custom profile and not add the default one.
        var secondManager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);

        // Assert
        Assert.Single(secondManager.Profiles);
        var loadedProfile = secondManager.Profiles.First();
        Assert.Equal("Custom Profile", loadedProfile.Name);
        Assert.Equal("192.168.1.100", loadedProfile.IpAddress);
        Assert.Equal(5020, loadedProfile.Port);
        Assert.Equal(2, loadedProfile.UnitId);
    }

    [Fact]
    public async Task ServerProfile_Reconnect_KeepsServiceInstanceAndDataStore()
    {
        // Regression: the transport match did not recognize a ModbusServerService
        // as fitting its server-mode profile, so every reconnect disposed the
        // running server and created a fresh one â€” wiping the register data
        // store between a stop/start cycle.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // free the port for the server to bind

        var serverProfile = new ConnectionProfile("Server", "127.0.0.1", port, 1)
        {
            Mode = "Server",
            ServerUnitIds = "1"
        };
        _manager.AddProfile(serverProfile);

        var started = await _manager.ConnectProfileAsync(serverProfile);
        Assert.True(started);

        var firstService = _manager.GetServiceForProfile(serverProfile);
        Assert.NotNull(firstService);
        Assert.IsAssignableFrom<ModbusServerService>(firstService);

        await firstService!.WriteSingleRegisterAsync(1, 1, 99);
        await _manager.DisconnectProfileAsync(serverProfile);

        // Act: reconnect.
        var restarted = await _manager.ConnectProfileAsync(serverProfile);
        Assert.True(restarted);

        var secondService = _manager.GetServiceForProfile(serverProfile);

        // Assert: same instance, and the written value survived the restart.
        Assert.Same(firstService, secondService);
        var values = await secondService!.ReadHoldingRegistersAsync(1, 1, 1);
        Assert.NotNull(values);
        Assert.Equal(99, values![0]);

        await _manager.DisconnectProfileAsync(serverProfile);
    }

    [Fact]
    public void LoadProfiles_WhenSavedActiveProfileIdIsStale_FallsBackToFirstProfile()
    {
        // Regression: a profiles file whose ActiveProfileId no longer exists
        // (profile removed, file hand-edited) left the app with profiles but
        // no active one, so every connection target resolved to null.
        var profilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModbusForge",
            "connection-profiles.json");

        var json = """
        {
          "ActiveProfileId": "ghost-profile-id",
          "Profiles": [
            {
              "Id": "real-profile",
              "Name": "Still Here",
              "IpAddress": "127.0.0.1",
              "Port": 1503,
              "UnitId": 1,
              "Mode": "Client",
              "ServerUnitIds": "1",
              "Transport": "Tcp",
              "ComPort": "COM1",
              "BaudRate": 9600,
              "Parity": "None",
              "DataBits": 8,
              "StopBits": "One",
              "RtsEnable": false
            }
          ]
        }
        """;
        var directory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(profilePath, json);

        try
        {
            var manager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);

            var profile = Assert.Single(manager.Profiles);
            Assert.Same(profile, manager.ActiveProfile);
            Assert.True(profile.IsActive);
        }
        finally
        {
            File.Delete(profilePath);
        }
    }

    [Fact]
    public async Task RemoveProfile_ConnectedServer_TearsDownAndReleasesPort()
    {
        // Regression: RemoveProfile fired a fire-and-forget disconnect and
        // immediately disposed the same service, racing the in-flight
        // DisconnectAsync. Removal of a connected server must complete the
        // teardown (disconnect, then dispose) and actually free the port.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // free the port for the server to bind

        var keepProfile = new ConnectionProfile("Keep", "127.0.0.1", 1, 1);
        var serverProfile = new ConnectionProfile("Server", "127.0.0.1", port, 1)
        {
            Mode = "Server",
            ServerUnitIds = "1"
        };
        _manager.AddProfile(keepProfile);
        _manager.AddProfile(serverProfile);
        _manager.SetActiveProfile(serverProfile);

        var started = await _manager.ConnectProfileAsync(serverProfile);
        Assert.True(started);
        Assert.Same(serverProfile, _manager.ActiveProfile);

        var disconnected = new List<ConnectionProfile>();
        _manager.ProfileDisconnected += (s, p) => disconnected.Add(p);

        // Act
        _manager.RemoveProfile(serverProfile);

        // Assert: gone from the list, active profile moved...
        Assert.DoesNotContain(serverProfile, _manager.Profiles);
        Assert.Same(keepProfile, _manager.ActiveProfile);

        // ...and the teardown finished: the port is bindable again.
        Assert.True(await WaitForPortToFreeAsync(port, TimeSpan.FromSeconds(5)),
            "Server port was still held after RemoveProfile â€” teardown did not complete.");

        // The disconnected event fires when the async teardown actually finishes.
        var eventWait = System.Diagnostics.Stopwatch.StartNew();
        while (!disconnected.Contains(serverProfile) && eventWait.Elapsed < TimeSpan.FromSeconds(2))
        {
            await Task.Delay(50);
        }
        Assert.Contains(serverProfile, disconnected);
        Assert.False(serverProfile.IsConnected);
    }

    private static async Task<bool> WaitForPortToFreeAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var test = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            try
            {
                test.Start();
                test.Stop();
                return true;
            }
            catch (System.Net.Sockets.SocketException)
            {
                await Task.Delay(100);
            }
        }
        return false;
    }

    [Fact]
    public async Task ServerProfile_CanStartServerAndClientCanReadAndWrite()
    {
        // Arrange
        var serverProfile = new ConnectionProfile("Server", "127.0.0.1", 1502, 1)
        {
            Mode = "Server",
            ServerUnitIds = "1,2"
        };
        var clientProfile = new ConnectionProfile("Client", "127.0.0.1", 1502, 1)
        {
            Mode = "Client"
        };

        _manager.AddProfile(serverProfile);
        _manager.AddProfile(clientProfile);

        // Act
        var serverStarted = await _manager.ConnectProfileAsync(serverProfile);
        Assert.True(serverStarted);

        var clientConnected = await _manager.ConnectProfileAsync(clientProfile);
        Assert.True(clientConnected);

        var clientService = _manager.GetServiceForProfile(clientProfile);
        Assert.NotNull(clientService);

        await clientService.WriteSingleRegisterAsync(1, 1, 42);

        var serverService = _manager.GetServiceForProfile(serverProfile);
        Assert.NotNull(serverService);

        var values = await serverService.ReadHoldingRegistersAsync(1, 1, 1);

        // Assert
        Assert.NotNull(values);
        Assert.Single(values);
        Assert.Equal(42, values[0]);

        // Cleanup
        await _manager.DisconnectProfileAsync(clientProfile);
        await _manager.DisconnectProfileAsync(serverProfile);
    }

    /// <summary>
    /// A dispatcher that claims the caller is always off its thread and queues
    /// posted work instead of running it, so tests can observe whether the
    /// manager marshalled the work rather than running it inline.
    /// </summary>
    private sealed class QueuedDispatcher : IDispatcher
    {
        private readonly Queue<Action> _queue = new();
        private readonly TaskCompletionSource _firstPost = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess => false;

        public void Invoke(Action action) => action();

        public T Invoke<T>(Func<T> func) => func();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());

        public void Post(Action action)
        {
            _queue.Enqueue(action);
            _firstPost.TrySetResult();
        }

        public Task WaitUntilPostedAsync() => _firstPost.Task;

        public void Drain()
        {
            while (_queue.Count > 0)
                _queue.Dequeue()();
        }
    }
}


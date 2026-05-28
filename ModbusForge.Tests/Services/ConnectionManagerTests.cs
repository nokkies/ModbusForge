using System;
using System.IO;
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
}

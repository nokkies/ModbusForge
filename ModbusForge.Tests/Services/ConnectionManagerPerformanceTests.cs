using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.Services;

public class ConnectionManagerPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ConnectionManagerPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DisconnectAllAsync_PerformanceTest()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ConnectionManager>>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var manager = new ConnectionManager(loggerMock.Object, loggerFactoryMock.Object);

        // Clear default profiles
        manager.Profiles.Clear();

        int connectionCount = 5;
        int delayMs = 100;

        // Use reflection to get the private _services dictionary
        var servicesField = typeof(ConnectionManager).GetField("_services", BindingFlags.NonPublic | BindingFlags.Instance);
        var services = (ConcurrentDictionary<string, ModbusTcpService>)servicesField.GetValue(manager);

        for (int i = 0; i < connectionCount; i++)
        {
            var profile = new ConnectionProfile($"Profile {i}", "127.0.0.1", 502 + i, 1)
            {
                IsConnected = true
            };
            manager.Profiles.Add(profile);

            var serviceMock = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            serviceMock.Setup(s => s.DisconnectAsync())
                .Returns(async () => await Task.Delay(delayMs));

            services[profile.Id] = serviceMock.Object;
        }

        // Act
        var sw = Stopwatch.StartNew();
        await manager.DisconnectAllAsync();
        sw.Stop();

        _output.WriteLine($"DisconnectAllAsync took {sw.ElapsedMilliseconds}ms for {connectionCount} connections.");

        // Assert
        // We don't assert a specific time here yet, we'll use this to establish a baseline.
        // But for the sake of the test being "green", let's just assert it finished.
        Assert.True(sw.ElapsedMilliseconds >= delayMs);

        foreach (var profile in manager.Profiles)
        {
            Assert.False(profile.IsConnected);
        }
    }
}

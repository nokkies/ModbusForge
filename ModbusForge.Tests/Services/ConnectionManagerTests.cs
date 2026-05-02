using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

public class ConnectionManagerTests
{
    private readonly Mock<ILogger<ConnectionManager>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly ConnectionManager _manager;

    public ConnectionManagerTests()
    {
        _mockLogger = new Mock<ILogger<ConnectionManager>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _manager = new ConnectionManager(_mockLogger.Object, _mockLoggerFactory.Object);

        // Remove default profiles added by constructor to start fresh for tests
        foreach (var profile in _manager.Profiles.ToList())
        {
            _manager.RemoveProfile(profile);
        }
        _manager.SetActiveProfile(null!);
    }

    [Fact]
    public void AddProfile_AddsProfileToCollection()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1);

        // Act
        _manager.AddProfile(profile);

        // Assert
        Assert.Contains(profile, _manager.Profiles);
    }

    [Fact]
    public void AddProfile_WhenActiveProfileIsNull_SetsActiveProfile()
    {
        // Arrange
        var profile = new ConnectionProfile("Test", "127.0.0.1", 502, 1);
        Assert.Null(_manager.ActiveProfile);

        // Act
        _manager.AddProfile(profile);

        // Assert
        Assert.Equal(profile, _manager.ActiveProfile);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public void AddProfile_WhenActiveProfileIsNotNull_DoesNotChangeActiveProfile()
    {
        // Arrange
        var firstProfile = new ConnectionProfile("First", "127.0.0.1", 502, 1);
        var secondProfile = new ConnectionProfile("Second", "127.0.0.1", 502, 1);

        _manager.AddProfile(firstProfile);
        Assert.Equal(firstProfile, _manager.ActiveProfile);

        // Act
        _manager.AddProfile(secondProfile);

        // Assert
        Assert.Equal(firstProfile, _manager.ActiveProfile);
        Assert.True(firstProfile.IsActive);
        Assert.False(secondProfile.IsActive);
    }
}

using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IApplicationLifetime"/> and <see cref="WpfApplicationLifetime"/>.
/// </summary>
public class ApplicationLifetimeTests
{
    [Fact]
    public void WpfApplicationLifetime_ImplementsInterface()
    {
        var lifetime = new WpfApplicationLifetime();
        Assert.IsAssignableFrom<IApplicationLifetime>(lifetime);
    }

    [Fact]
    public void MockApplicationLifetime_Shutdown_CanBeVerified()
    {
        // Verify the interface contract works correctly with mocks,
        // which is how MainWindow will be tested.
        var mock = new Mock<IApplicationLifetime>();

        mock.Object.Shutdown();

        mock.Verify(l => l.Shutdown(), Times.Once);
    }

    [Fact]
    public void MockApplicationLifetime_Shutdown_NotCalledByDefault()
    {
        var mock = new Mock<IApplicationLifetime>();

        mock.Verify(l => l.Shutdown(), Times.Never);
    }
}

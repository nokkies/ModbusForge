using System;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ShellWindowService"/>.
/// Because the implementation creates real WPF windows, these tests focus on
/// constructor null-guard behaviour rather than Show* methods (which require
/// a WPF dispatcher and a real <see cref="System.Windows.Window"/>).
/// </summary>
public class ShellWindowServiceTests
{
    private readonly Mock<ILogger<AboutWindow>> _aboutLogger = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly HelpViewModel _helpViewModel;
    private readonly Mock<IScriptRunner> _scriptRunner = new();
    private readonly Mock<IModbusService> _modbusService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IConnectionManager> _connectionManager = new();
    private readonly Mock<IFileDialogService> _fileDialogService = new();
    private readonly Mock<IDispatcher> _dispatcher = new();

    public ShellWindowServiceTests()
    {
        // HelpViewModel is a partial class with source-generated properties;
        // create a real instance with mocked dependencies.
        _helpViewModel = new HelpViewModel(
            new Mock<IHelpContentService>().Object,
            new Mock<ILogger<HelpViewModel>>().Object);
    }

    private ShellWindowService CreateService() =>
        new ShellWindowService(
            _aboutLogger.Object,
            _dialogService.Object,
            _helpViewModel,
            _scriptRunner.Object,
            _modbusService.Object,
            _settingsService.Object,
            _connectionManager.Object,
            _fileDialogService.Object,
            _dispatcher.Object);

    [Fact]
    public void Constructor_WithAllDependencies_Succeeds()
    {
        var service = CreateService();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_NullAboutLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                null!,
                _dialogService.Object,
                _helpViewModel,
                _scriptRunner.Object,
                _modbusService.Object,
                _settingsService.Object,
                _connectionManager.Object,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Constructor_NullDialogService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                _aboutLogger.Object,
                null!,
                _helpViewModel,
                _scriptRunner.Object,
                _modbusService.Object,
                _settingsService.Object,
                _connectionManager.Object,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Constructor_NullHelpViewModel_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                _aboutLogger.Object,
                _dialogService.Object,
                null!,
                _scriptRunner.Object,
                _modbusService.Object,
                _settingsService.Object,
                _connectionManager.Object,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Constructor_NullScriptRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                _aboutLogger.Object,
                _dialogService.Object,
                _helpViewModel,
                null!,
                _modbusService.Object,
                _settingsService.Object,
                _connectionManager.Object,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Constructor_NullModbusService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                _aboutLogger.Object,
                _dialogService.Object,
                _helpViewModel,
                _scriptRunner.Object,
                null!,
                _settingsService.Object,
                _connectionManager.Object,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Constructor_NullSettingsService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                _aboutLogger.Object,
                _dialogService.Object,
                _helpViewModel,
                _scriptRunner.Object,
                _modbusService.Object,
                null!,
                _connectionManager.Object,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Constructor_NullConnectionManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ShellWindowService(
                _aboutLogger.Object,
                _dialogService.Object,
                _helpViewModel,
                _scriptRunner.Object,
                _modbusService.Object,
                _settingsService.Object,
                null!,
                _fileDialogService.Object,
                _dispatcher.Object));
    }

    [Fact]
    public void Service_ImplementsIShellWindowService()
    {
        var service = CreateService();
        Assert.IsAssignableFrom<IShellWindowService>(service);
    }
}

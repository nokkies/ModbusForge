using System;
using System.Threading;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using ModbusForge.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.SmokeTests;

public class HelpSystemSmokeTests : IDisposable
{
    private readonly FlaUiAppHelper _app;
    private readonly ITestOutputHelper _output;
    private bool _disposed;

    public HelpSystemSmokeTests(ITestOutputHelper output)
    {
        _output = output;
        _app = FlaUiAppHelper.LaunchFromProject();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _app.Dispose();
    }

    [Fact]
    public void HelpWindow_Opens_FromF1Key()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();
        Keyboard.Press(VirtualKeyShort.F1);

        var helpWindow = _app.WaitForWindowByTitle("Help");
        Assert.NotNull(helpWindow);
        Assert.True(helpWindow.IsEnabled, "Help window should be enabled.");

        _output.WriteLine("Help window opened successfully from F1 key.");
        _app.CloseWindow(helpWindow);
    }

    [Fact]
    public void HelpWindow_Opens_FromHelpMenu()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        _app.ClickMenuItem(mainWindow, "Help", "Help...");

        var helpWindow = _app.WaitForWindowByTitle("Help");
        Assert.NotNull(helpWindow);

        _output.WriteLine("Help window opened successfully from Help menu.");
        _app.CloseWindow(helpWindow);
    }

    [Fact]
    public void KeyboardShortcutsWindow_Opens_FromHelpMenu()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        _app.ClickMenuItem(mainWindow, "Help", "Keyboard Shortcuts...");

        var shortcutsWindow = _app.WaitForWindowByTitle("Keyboard Shortcuts");
        Assert.NotNull(shortcutsWindow);

        _output.WriteLine("Keyboard Shortcuts window opened successfully.");
        _app.CloseWindow(shortcutsWindow);
    }

    [Fact]
    public void TroubleshootingWindow_Opens_FromHelpMenu()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        _app.ClickMenuItem(mainWindow, "Help", "Troubleshooting...");

        var troubleshootingWindow = _app.WaitForWindowByTitle("Troubleshooting");
        Assert.NotNull(troubleshootingWindow);

        _output.WriteLine("Troubleshooting window opened successfully.");
        _app.CloseWindow(troubleshootingWindow);
    }

    [Fact]
    public void ScriptEditorWindow_Opens_FromOptionsMenu()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        _app.ClickMenuItem(mainWindow, "Options", "Script Editor...");

        var scriptEditorWindow = _app.WaitForWindowByTitle("Script Editor");
        Assert.NotNull(scriptEditorWindow);

        _output.WriteLine("Script Editor window opened successfully from Options menu.");
        _app.CloseWindow(scriptEditorWindow);
    }

    [Fact]
    public void AboutWindow_Opens_FromHelpMenu()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        _app.ClickMenuItem(mainWindow, "Help", "About");

        var aboutWindow = _app.WaitForWindowByTitle("About ModbusForge");
        Assert.NotNull(aboutWindow);

        _output.WriteLine("About window opened successfully.");
        _app.CloseWindow(aboutWindow);
    }

    [Fact]
    public void MainWindow_ContainsVersionInTitle()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        var title = mainWindow.Title;

        Assert.Contains("ModbusForge", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5.7", title, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"Main window title: {title}");
    }

    [Fact]
    public void MainWindow_ContainsNavigationItems()
    {
        var mainWindow = _app.GetMainWindowOrThrow();

        var registersItem = _app.FindElementByName(mainWindow, "Registers");
        var decodeItem = _app.FindElementByName(mainWindow, "Decode");
        var trendItem = _app.FindElementByName(mainWindow, "Trend");
        var consoleItem = _app.FindElementByName(mainWindow, "Console");

        Assert.NotNull(registersItem);
        Assert.NotNull(decodeItem);
        Assert.NotNull(trendItem);
        Assert.NotNull(consoleItem);

        _output.WriteLine("Main navigation items found successfully.");
    }
}

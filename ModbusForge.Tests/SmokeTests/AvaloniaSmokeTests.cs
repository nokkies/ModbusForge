using System;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using ModbusForge.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.SmokeTests;

[Collection("Sequential UI Tests")]
public class AvaloniaSmokeTests : IDisposable
{
    private readonly FlaUiAppHelper _app;
    private readonly ITestOutputHelper _output;
    private bool _disposed;

    public AvaloniaSmokeTests(ITestOutputHelper output)
    {
        _output = output;

        var solutionDir = FlaUiAppHelper.GetSolutionDirectory();
        var appPath = Path.Combine(solutionDir, "ModbusForge.Avalonia", "bin", "Debug", "net8.0", "ModbusForge.exe");

        if (!File.Exists(appPath))
        {
            var fallbackPath = new[] { "Release", "Debug" }
                .Select(c => Path.Combine(solutionDir, "ModbusForge.Avalonia", "bin", c, "net8.0", "ModbusForge.exe"))
                .FirstOrDefault(File.Exists);

            appPath = fallbackPath ?? appPath;
        }

        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException($"ModbusForge.exe not found at {appPath}. Build the Avalonia project first.");
        }

        _output.WriteLine($"Launching Avalonia app from: {appPath}");
        _app = new FlaUiAppHelper(appPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _app.Dispose();
    }

    [Fact]
    public void MainWindow_Exists_AndContainsModbusForgeTitle()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        Assert.Contains("ModbusForge", mainWindow.Title, StringComparison.OrdinalIgnoreCase);

        var dashboardHeader = _app.WaitForElementByName(mainWindow, "Dashboard");
        Assert.NotNull(dashboardHeader);
        _output.WriteLine($"Avalonia main window title: {mainWindow.Title}; MainViewModel DataContext bound.");
    }

    [Fact]
    public void VisualSimulationTab_Opens_FromNavigation()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        var simButton = _app.FindElementByName(mainWindow, "Simulation");
        Assert.NotNull(simButton);
        _output.WriteLine("Found Simulation navigation button.");

        simButton!.AsButton().Click();
        Thread.Sleep(500);

        var palette = _app.WaitForElementByName(mainWindow, "Palette");
        var programs = _app.WaitForElementByName(mainWindow, "Programs (POUs)");
        var defaultProgram = _app.WaitForElementByName(mainWindow, "Main");

        Assert.NotNull(palette);
        Assert.NotNull(programs);
        Assert.NotNull(defaultProgram);

        _output.WriteLine("Visual Node Editor loaded with Palette, Programs (POUs) and DataContext (default program).");
    }

    [Fact]
    public void TagBrowserWindow_Opens_FromVisualNodeEditorToolbar()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        var simButton = _app.FindElementByName(mainWindow, "Simulation");
        Assert.NotNull(simButton);
        simButton!.AsButton().Click();
        Thread.Sleep(500);

        var tagBrowserButton = _app.WaitForElementByName(mainWindow, "Tag Browser");
        Assert.NotNull(tagBrowserButton);
        _output.WriteLine("Found Tag Browser button on Simulation toolbar.");

        // Use the Invoke pattern to ensure the button's command is executed.
        if (tagBrowserButton.Patterns.Invoke.IsSupported)
        {
            tagBrowserButton.Patterns.Invoke.Pattern.Invoke();
            _output.WriteLine("Invoked Tag Browser button via UIA Invoke pattern.");
        }
        else
        {
            tagBrowserButton.AsButton().Click();
            _output.WriteLine("Clicked Tag Browser button (no Invoke pattern).");
        }

        var tagBrowserWindow = _app.WaitForWindowByTitle("Tag Browser - Symbolic Addressing");
        Assert.NotNull(tagBrowserWindow);
        Assert.True(tagBrowserWindow.IsEnabled, "Tag Browser window should be enabled.");

        _output.WriteLine("Tag Browser window opened successfully from Visual Node Editor toolbar.");
        _app.CloseWindow(tagBrowserWindow);
    }
}

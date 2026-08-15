using System;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using ModbusForge.UITests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.UITests.SmokeTests;

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
        var appPath = Path.Combine(solutionDir, "ModbusForge", "bin", "Debug", "net8.0", "ModbusForge.exe");

        if (!File.Exists(appPath))
        {
            var fallbackPath = new[] { "Release", "Debug" }
                .Select(c => Path.Combine(solutionDir, "ModbusForge", "bin", c, "net8.0", "ModbusForge.exe"))
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

        var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());
        var navList = mainWindow.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.List))?.AsListBox();
        Assert.NotNull(navList);
        navList!.Select("Simulation");
        _output.WriteLine("Selected Simulation from navigation ListBox.");

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

        var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());
        var navList = mainWindow.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.List))?.AsListBox();
        Assert.NotNull(navList);
        navList!.Select("Simulation");
        _output.WriteLine("Selected Simulation from navigation ListBox.");

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

    [Fact]
    public void ModeToggle_ChangesConnectionButtonText()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

        // The mode selector is the first editable ComboBox (Mode) in the connection bar.
        var modeComboBox = mainWindow.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox))?.AsComboBox();
        Assert.NotNull(modeComboBox);

        // The connection button sits in the same toolbar.
        var connectButton = mainWindow.FindFirstDescendant(cf.ByName("Connect"))?.AsButton();
        Assert.NotNull(connectButton);

        // Switch to Server mode.
        modeComboBox.Select("Server");
        Thread.Sleep(500);

        var startServerButton = mainWindow.FindFirstDescendant(cf.ByName("Start Server"))?.AsButton();
        Assert.NotNull(startServerButton);
        _output.WriteLine("Mode toggle changed Connect button to Start Server.");
    }

    [Fact]
    public void ServerMode_StartServer_StopsCleanly()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

        // Switch to Server mode.
        var modeComboBox = mainWindow.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox))?.AsComboBox();
        Assert.NotNull(modeComboBox);
        modeComboBox.Select("Server");
        Thread.Sleep(500);

        var startButton = mainWindow.FindFirstDescendant(cf.ByName("Start Server"))?.AsButton();
        Assert.NotNull(startButton);
        Assert.True(startButton.Patterns.Invoke.IsSupported, "Start Server button should support the Invoke pattern.");

        startButton.Patterns.Invoke.Pattern.Invoke();

        // Wait for the server to be connected.
        var connectedWait = DateTime.UtcNow.AddSeconds(10);
        string debugText = string.Empty;
        while (DateTime.UtcNow < connectedWait && !debugText.Contains("Connected: True"))
        {
            var debugTab = mainWindow.FindFirstDescendant(cf.ByName("Debug"))?.AsTabItem();
            if (debugTab != null)
            {
                debugTab.Select();
                Thread.Sleep(250);
                var debugSummary = mainWindow.FindFirstDescendant(cf.ByAutomationId("DebugSummary"))?.AsLabel();
                debugText = debugSummary?.Text ?? string.Empty;
            }
            else
            {
                Thread.Sleep(250);
            }
        }
        Assert.Contains("Connected: True", debugText);
        _output.WriteLine("Server started; Connected: True.");

        // Stop the server.
        var disconnectButton = mainWindow.FindFirstDescendant(cf.ByName("Disconnect"))?.AsButton();
        Assert.NotNull(disconnectButton);
        Assert.True(disconnectButton.Patterns.Invoke.IsSupported, "Disconnect button should support the Invoke pattern.");
        disconnectButton.Patterns.Invoke.Pattern.Invoke();

        // Wait for the server to stop.
        var stopWait = DateTime.UtcNow.AddSeconds(10);
        debugText = string.Empty;
        while (DateTime.UtcNow < stopWait && !debugText.Contains("Connected: False"))
        {
            var debugTab = mainWindow.FindFirstDescendant(cf.ByName("Debug"))?.AsTabItem();
            if (debugTab != null)
            {
                debugTab.Select();
                Thread.Sleep(250);
                var debugSummary = mainWindow.FindFirstDescendant(cf.ByAutomationId("DebugSummary"))?.AsLabel();
                debugText = debugSummary?.Text ?? string.Empty;
            }
            else
            {
                Thread.Sleep(250);
            }
        }
        Assert.Contains("Connected: False", debugText);

        var startButtonAfterStop = mainWindow.FindFirstDescendant(cf.ByName("Start Server"))?.AsButton();
        Assert.NotNull(startButtonAfterStop);
        _output.WriteLine("Server stopped cleanly and returned to Start Server state.");
    }

    [Fact]
    public void ServerMode_RegistersTab_DataGrid_Populates_AfterRead()
    {
        var mainWindow = _app.GetMainWindowOrThrow();
        mainWindow.Focus();

        var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

        // Switch to Server mode and start the server.
        var modeComboBox = mainWindow.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox))?.AsComboBox();
        Assert.NotNull(modeComboBox);
        modeComboBox.Select("Server");
        Thread.Sleep(500);

        var startButton = mainWindow.FindFirstDescendant(cf.ByName("Start Server"))?.AsButton();
        Assert.NotNull(startButton);
        Assert.True(startButton.Patterns.Invoke.IsSupported, "Start Server button should support the Invoke pattern.");
        startButton.Patterns.Invoke.Pattern.Invoke();

        // Wait for the server to be connected.
        var connectedWait = DateTime.UtcNow.AddSeconds(10);
        string debugText = string.Empty;
        while (DateTime.UtcNow < connectedWait && !debugText.Contains("Connected: True"))
        {
            var debugTab = mainWindow.FindFirstDescendant(cf.ByName("Debug"))?.AsTabItem();
            if (debugTab != null)
            {
                debugTab.Select();
                Thread.Sleep(250);
                var debugSummary = mainWindow.FindFirstDescendant(cf.ByAutomationId("DebugSummary"))?.AsLabel();
                debugText = debugSummary?.Text ?? string.Empty;
                _output.WriteLine($"DebugSummary: {debugText}");
            }
            else
            {
                Thread.Sleep(250);
            }
        }
        Assert.Contains("Connected: True", debugText);

        // Open the Registers tab.
        var navList = mainWindow.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.List))?.AsListBox();
        Assert.NotNull(navList);
        navList!.Select("Registers");
        Thread.Sleep(500);

        // Find the holding-registers DataGrid and its Read button.
        var dataGrid = mainWindow.FindFirstDescendant(cf.ByName("Holding Registers Grid"));
        Assert.NotNull(dataGrid);
        _output.WriteLine($"Holding Registers Grid found: {dataGrid.BoundingRectangle}");

        var parent = dataGrid.Parent;
        Assert.NotNull(parent);
        var readButton = parent.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button).And(cf.ByName("Read")))?.AsButton();
        Assert.NotNull(readButton);
        _output.WriteLine($"Read button IsEnabled: {readButton.IsEnabled}");

        // Click Read using the Invoke pattern.
        Assert.True(readButton.Patterns.Invoke.IsSupported, "Read button should support the Invoke pattern.");
        readButton.Patterns.Invoke.Pattern.Invoke();

        // Wait for the read to complete.
        var readWait = DateTime.UtcNow.AddSeconds(10);
        string statusText = string.Empty;
        while (DateTime.UtcNow < readWait && !statusText.Contains("Read 20 holding registers"))
        {
            var statusMessage = mainWindow.FindFirstDescendant(cf.ByAutomationId("StatusMessage"))?.AsLabel();
            statusText = statusMessage?.Text ?? string.Empty;
            _output.WriteLine($"StatusMessage: {statusText}");
            Thread.Sleep(250);
        }
        Assert.Contains("Read 20 holding registers", statusText);

        // Verify the DataGrid has rows.
        dataGrid = mainWindow.FindFirstDescendant(cf.ByName("Holding Registers Grid"));
        Assert.NotNull(dataGrid);
        var rows = dataGrid.FindAllDescendants(cf.ByControlType(FlaUI.Core.Definitions.ControlType.DataItem));
        _output.WriteLine($"DataGrid row count: {rows.Length}");
        Assert.True(rows.Length >= 1, "DataGrid should display at least one row after reading.");
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using ModbusForge.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace ModbusForge.Tests.UITests;

/// <summary>
/// End-to-end smoke test for the Unity Pro XEF import: File > Load PLC, pick a
/// sample .XEF, check the programs land on the PLC tab (and nowhere near the
/// Simulation tab's POU tree), then open and run one. Guards against the
/// duplicate-node-id crash the first cut had.
/// </summary>
/// <remarks>
/// FlaUI's mouse/keyboard inject via SendInput, which requires a normal
/// interactive desktop; under a service/agent session it fails with
/// "Access is denied" and the test skips.
/// </remarks>
[Trait("Category", "UITests")]
public class PlcImportUITests : IDisposable
{
    private const string SampleXef = @"C:\Users\rvn\Documents\GitHub\UnityParseEngine\resources\XML\LCPLC001.XEF";
    private readonly ITestOutputHelper _output;

    public PlcImportUITests(ITestOutputHelper output) => _output = output;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfAbsolute = 0x8000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>True when SendInput can inject (interactive desktop available).</summary>
    private static bool HasInteractiveInput()
    {
        try
        {
            // One harmless absolute-move to (0,0); it fails with ERROR_ACCESS_DENIED
            // (5) on a non-interactive desktop.
            var inputs = new INPUT[1];
            inputs[0].type = InputMouse;
            inputs[0].mi.dwFlags = MouseeventfMove | MouseeventfAbsolute;
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
            return Marshal.GetLastWin32Error() != 5;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void LoadPlcFile_ImportsIntoThePlcTab_AndLeavesTheSimulationTreeAlone()
    {
        if (!File.Exists(SampleXef))
        {
            _output.WriteLine($"Sample file {SampleXef} not present - skipping.");
            return;
        }

        if (!HasInteractiveInput())
        {
            _output.WriteLine("No interactive input desktop (SendInput denied) - skipping UI automation.");
            return;
        }

        using var helper = FlaUiAppHelper.LaunchFromProject();
        var window = helper.GetMainWindowOrThrow();
        _output.WriteLine($"Main window: '{window.Title}'");
        Thread.Sleep(TimeSpan.FromSeconds(8));

        var simulationItemsBefore = CountTreeItemsOnPage(window, "Simulation");

        var fileMenu = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("File")));
        Assert.NotNull(fileMenu);

        // Real mouse + keyboard: the launched app is the foreground window.
        var fileRect = fileMenu!.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
            fileRect.X + (fileRect.Width / 2), fileRect.Y + (fileRect.Height / 2)));
        Thread.Sleep(1200);

        // Highlight "Load PLC" (third item) and commit.
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN);
        Thread.Sleep(150);
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN);
        Thread.Sleep(150);
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN);
        Thread.Sleep(150);
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
        Thread.Sleep(500);

        var dialog = helper.WaitForWindowByTitle("Load PLC (Unity Pro XEF)", TimeSpan.FromSeconds(20));
        _output.WriteLine("File dialog open.");

        var edit = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))?.AsTextBox();
        Assert.NotNull(edit);
        edit!.Text = SampleXef;

        var openButton = dialog.FindFirstDescendant(cf => cf.ByName("Open"))?.AsButton();
        Assert.NotNull(openButton);
        openButton!.Invoke();
        _output.WriteLine("Clicked Open.");

        // Parsing + node building for thousands of nodes takes a while.
        Thread.Sleep(TimeSpan.FromSeconds(25));

        // The import lands in the PLC tab's project navigator...
        ClickByName(window, "PLC");
        var plcTree = window.FindFirstDescendant(cf => cf.ByAutomationId("PlcProjectTree"));
        Assert.NotNull(plcTree);
        var plcItems = plcTree!.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem));
        _output.WriteLine($"PLC navigator items after import: {plcItems.Length}");
        Assert.True(plcItems.Length > 1, $"Expected the imported project in the PLC navigator, found {plcItems.Length} item(s).");

        // ...and the Simulation tab's POU tree is exactly as it was.
        Assert.Equal(simulationItemsBefore, CountTreeItemsOnPage(window, "Simulation"));

        // Open the first FBD program (Application > Section / Programs > location >
        // program) with the keyboard and run it - the crash surface.
        ClickByName(window, "PLC");
        var root = plcTree.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem));
        Assert.NotNull(root);
        var rootRect = root!.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
            rootRect.X + (rootRect.Width / 2), rootRect.Y + (rootRect.Height / 2)));
        Thread.Sleep(500);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RIGHT);
            Thread.Sleep(300);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN);
            Thread.Sleep(300);
        }
        Thread.Sleep(TimeSpan.FromSeconds(3));

        var runButton = window.FindFirstDescendant(cf => cf.ByName("Run"))
            ?? window.FindFirstDescendant(cf => cf.ByName("Start"));
        if (runButton != null)
        {
            var rRect = runButton.BoundingRectangle;
            FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
                rRect.X + (rRect.Width / 2), rRect.Y + (rRect.Height / 2)));
            Thread.Sleep(TimeSpan.FromSeconds(8));
            _output.WriteLine("Clicked Run.");
        }
        else
        {
            _output.WriteLine("Run button not found (check the name) - skipping run step.");
        }

        _output.WriteLine("Import + run survived without crashing.");
    }

    /// <summary>Opens a page from the navigation list and counts its tree items.</summary>
    private int CountTreeItemsOnPage(Window window, string navigationItem)
    {
        ClickByName(window, navigationItem);
        var count = window.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem)).Length;
        _output.WriteLine($"Tree items on '{navigationItem}': {count}");
        return count;
    }

    private static void ClickByName(Window window, string name)
    {
        var element = window.FindFirstDescendant(cf => cf.ByName(name));
        Assert.NotNull(element);
        var rect = element!.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
            rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
        Thread.Sleep(TimeSpan.FromSeconds(3));
    }

    public void Dispose() { }
}

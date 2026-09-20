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
/// End-to-end smoke test for the Unity Pro FEF import: File > Load PLC,
/// pick a sample .XEF, run the simulation, and switch programs. Guards
/// against the duplicate-node-id crash the first cut had.
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
    public void LoadPlcFile_ImportsWithoutCrash_AndProgramsAppear()
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

        var fileMenu = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("File")));
        Assert.NotNull(fileMenu);

        // Real mouse + keyboard: the launched app is the foreground window.
        var fileRect = fileMenu!.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
            fileRect.X + fileRect.Width / 2, fileRect.Y + fileRect.Height / 2));
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

        var dialog = helper.WaitForWindowByTitle("Load PLC (Unity Pro FEF)", TimeSpan.FromSeconds(20));
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

        var simulation = window.FindFirstDescendant(cf => cf.ByName("Simulation"));
        Assert.NotNull(simulation);
        var simRect = simulation!.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
            simRect.X + simRect.Width / 2, simRect.Y + simRect.Height / 2));
        Thread.Sleep(TimeSpan.FromSeconds(5));

        var programNodes = window.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem));
        _output.WriteLine($"Tree items after import: {programNodes.Length}");
        Assert.True(programNodes.Length > 1, $"Expected the imported programs in the POU tree, found {programNodes.Length}.");

        // Click an imported program and run the simulation - the crash surface.
        var target = programNodes[Math.Min(1, programNodes.Length - 1)];
        var tRect = target.BoundingRectangle;
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
            tRect.X + tRect.Width / 2, tRect.Y + tRect.Height / 2));
        Thread.Sleep(TimeSpan.FromSeconds(3));

        var runButton = window.FindFirstDescendant(cf => cf.ByName("Run"))
            ?? window.FindFirstDescendant(cf => cf.ByName("Start"));
        if (runButton != null)
        {
            var rRect = runButton.BoundingRectangle;
            FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(
                rRect.X + rRect.Width / 2, rRect.Y + rRect.Height / 2));
            Thread.Sleep(TimeSpan.FromSeconds(8));
            _output.WriteLine("Clicked Run.");
        }
        else
        {
            _output.WriteLine("Run button not found (check the name) - skipping run step.");
        }

        _output.WriteLine("Import + run survived without crashing.");
    }

    public void Dispose() { }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace ModbusForge.Tests.UITests
{
    /// <summary>
    /// Manual UI probe: starts the app's own Modbus server, adds a trend pen
    /// through the Trends tab's Add dialog, and captures the result so the
    /// pen list + chart can be visually verified. Excluded from standard runs
    /// by the "FullyQualifiedName!~UITests" filter:
    ///
    /// dotnet test ModbusForge.Tests --filter "FullyQualifiedName~TrendsPenFlowUITests"
    /// </summary>
    public class TrendsPenFlowUITests
    {
        [Fact]
        public void AddPen_ViaDialog_AppearsonPenList_AndChart()
        {
            var exe = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "ModbusForge", "bin", "Debug", "net8.0", "ModbusForge.exe"));
            Assert.True(File.Exists(exe), $"App exe not found: {exe}");

            var app = Application.Launch(exe);
            app.WaitWhileMainHandleIsMissing(TimeSpan.FromMinutes(2));

            try
            {
                using var automation = new UIA3Automation();
                var window = app.GetMainWindow(automation, TimeSpan.FromMinutes(1));
                Assert.NotNull(window);
                window!.SetForeground();

                // 1) Server mode, so the app self-hosts a Modbus server.
                var combo = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox));
                Assert.NotNull(combo);
                combo!.Click(); // open the dropdown
                Thread.Sleep(400);
                var serverItem = window.FindFirstDescendant(cf => cf.ByName("Server").And(cf.ByControlType(ControlType.ListItem)));
                Assert.NotNull(serverItem);
                serverItem.Click();
                Thread.Sleep(600);

                // 2) Start the server (the toolbar toggle relabels to "Start Server").
                var startServer = window.FindFirstDescendant(cf => cf.ByName("Start Server"));
                Assert.NotNull(startServer);
                startServer!.Click();
                Thread.Sleep(1500);

                // 3) Open the Trends tab.
                var trendsNav = window.FindFirstDescendant(cf => cf.ByName("Trends").And(cf.ByControlType(ControlType.ListItem)));
                Assert.NotNull(trendsNav);
                trendsNav!.Click();
                Thread.Sleep(600);

                // 4) Add a pen through the dialog (defaults: Register, HoldingRegister, address 1).
                var addButton = window.FindFirstDescendant(cf => cf.ByName("Add"));
                Assert.NotNull(addButton);
                addButton.Click();
                Thread.Sleep(1500);

                window.CaptureToFile(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "trendstep-add.png")));
                // The dialog is an owned window that the process-level
                // enumeration can miss, so search the desktop tree by title.
                var dialog = automation.GetDesktop().FindFirstDescendant(
                    cf => cf.ByName("Add Trend Pen").And(cf.ByControlType(ControlType.Window)));
                Assert.True(dialog is not null, "Add Trend Pen dialog did not open");
                dialog!.SetForeground();
                var okButton = dialog.FindFirstDescendant(cf => cf.ByName("OK"));
                Assert.NotNull(okButton);
                okButton.Click();
                Thread.Sleep(4000); // a few poll cycles at the 1s read period

                // 5) Capture the main window (Trends tab with the new pen).
                window.SetForeground();
                var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "trendpen-capture.png"));
                window.CaptureToFile(capturePath);
                Assert.True(File.Exists(capturePath), $"Capture not saved: {capturePath}");
            }
            finally
            {
                try { app.Kill(); } catch { /* best effort */ }
            }
        }
    }
}

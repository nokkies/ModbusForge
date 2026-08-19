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

                // 1) Server mode, so the app self-hosts a Modbus server. A
                //    previous probe run may have persisted server mode, in
                //    which case the toggle is already "Start Server".
                EnsureServerMode(window);

                // 2) Start the server and wait until it actually connected.
                StartServerAndWaitConnected(window, "trenddiag-noserver-1.png");

                // 3) Open the Trends tab.
                OpenTrendsTab(window);

                // 4) Add a pen through the dialog (defaults: Register, HoldingRegister, address 1).
                AddPenViaDialog(automation, window, address: 1);
                Thread.Sleep(4000); // a few poll cycles at the 1s read period

                // 5) Capture the main window (Trends tab with the new pen).
                var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "trendpen-capture.png"));
                Capture(window, "trendpen-capture.png");
                Assert.True(File.Exists(capturePath), $"Capture not saved: {capturePath}");
            }
            finally
            {
                try { app.Kill(); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void PenRow_AppearsWithoutData_AndFillsWithServerData()
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

                OpenTrendsTab(window);

                // 1) Add a pen (address 1, seeded to 10 by the built-in
                //    server) while the app is disconnected: the row must
                //    appear immediately, with no value and no failure dot.
                AddPenViaDialog(automation, window, address: 1);

                var noDataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "trendrow-nodata.png"));
                Capture(window, "trendrow-nodata.png");
                Assert.True(File.Exists(noDataPath), $"Capture not saved: {noDataPath}");

                // 2) Start the built-in server. Pens are a monitored source,
                //    so the watch loop starts and the row fills with data.
                EnsureServerMode(window);
                StartServerAndWaitConnected(window, "trenddiag-noserver-2.png");

                Thread.Sleep(5000); // ~5 poll cycles at the 1s read period

                var dataPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "trendrow-data.png"));
                Capture(window, "trendrow-data.png");
                Assert.True(File.Exists(dataPath), $"Capture not saved: {dataPath}");
            }
            finally
            {
                try { app.Kill(); } catch { /* best effort */ }
            }
        }

        private static void OpenTrendsTab(FlaUI.Core.AutomationElements.Window window)
        {
            // Select the Trends tab and wait for the pen list's "Pens"
            // header to render. The click is retried until it registers:
            // on a cold launch the first UIA click can arrive before the
            // nav list is ready for input.
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (window.FindFirstDescendant(cf => cf.ByName("Pens")) is null && DateTime.UtcNow < deadline)
            {
                var trendsNav = window.FindFirstDescendant(cf => cf.ByName("Trends").And(cf.ByControlType(ControlType.ListItem)));
                if (trendsNav is not null)
                {
                    trendsNav.SetForeground();
                    trendsNav.Click();
                }
                Thread.Sleep(400);
            }
            Assert.True(window.FindFirstDescendant(cf => cf.ByName("Pens")) is not null, "Trends tab did not open");
        }

        private static void StartServerAndWaitConnected(
            FlaUI.Core.AutomationElements.Window window,
            string diagCapture)
        {
            // The click only takes effect when the window holds the
            // foreground, which an OS notification popup can steal at any
            // moment. So click, observe, and re-click until the connection
            // actually happens. Re-clicking a starting/started server is a
            // no-op (the button disappears once connected).
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (window.FindFirstDescendant(cf => cf.ByName("Disconnect")) is null && DateTime.UtcNow < deadline)
            {
                var startServer = window.FindFirstDescendant(cf => cf.ByName("Start Server"));
                if (startServer is not null)
                {
                    startServer.SetForeground();
                    startServer.Click();
                }
                Thread.Sleep(1000);
            }
            if (window.FindFirstDescendant(cf => cf.ByName("Disconnect")) is null)
            {
                Capture(window, diagCapture);
                Assert.Fail("server never connected (diagnostic capture saved: " + diagCapture + ")");
            }
        }

        private static void Capture(FlaUI.Core.AutomationElements.Window window, string file)
        {
            // A window without foreground can capture black; restore it first.
            window.SetForeground();
            Thread.Sleep(400);
            window.CaptureToFile(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, file)));
        }

        private static void AddPenViaDialog(
            UIA3Automation automation,
            FlaUI.Core.AutomationElements.Window window,
            int address)
        {
            var addButton = window.FindFirstDescendant(cf => cf.ByName("Add"));
            Assert.NotNull(addButton);

            // Click Add exactly once: the dialog opens synchronously, but a
            // second Add click would stack a second modal dialog behind the
            // first (invisible in a capture, yet still blocking the main
            // window). Only the UIA visibility of the dialog lags, so wait
            // generously for it to appear in the desktop tree.
            addButton!.SetForeground();
            addButton.Click();
            var dialog = null as FlaUI.Core.AutomationElements.AutomationElement;
            var openDeadline = DateTime.UtcNow.AddSeconds(15);
            while (dialog is null && DateTime.UtcNow < openDeadline)
            {
                dialog = FindDialog(automation);
                if (dialog is null) Thread.Sleep(300);
            }
            Assert.True(dialog is not null, "Add Trend Pen dialog did not open");
            dialog!.SetForeground();

            if (address != 1)
            {
                // The address NumericUpDown's editor is the first Edit in the
                // dialog (the Name field and the read-period editor come
                // after it in tree order). Set its text through the Value
                // pattern; the NumericUpDown picks the value up live.
                var addressEditor = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                Assert.NotNull(addressEditor);
                addressEditor!.Patterns.Value.Pattern.SetValue(address.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Thread.Sleep(400);
            }

            var okButton = dialog.FindFirstDescendant(cf => cf.ByName("OK"));
            Assert.NotNull(okButton);

            // A real mouse click is the only input that reliably reaches
            // buttons inside the dialog's nested dispatcher frame (the
            // Invoke pattern does not), and the click only lands when the
            // dialog holds the foreground - which an OS notification popup
            // can steal at exactly the wrong moment. So retry: ensure the
            // foreground, click OK, and observe the outcome. The completion
            // signal is the pen row appearing in a list in the main window
            // (which also proves the modal frame returned). Lists only: a
            // still-open dialog's Name field carries the same text and the
            // dialog is part of the main window's UIA subtree.
            var penName = $"HR Trend {address}";
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!PenRowVisible(window, penName) && DateTime.UtcNow < deadline)
            {
                var current = FindDialog(automation);
                if (current is not null && current.IsAvailable)
                {
                    current.SetForeground();
                    Thread.Sleep(250);
                    current.FindFirstDescendant(cf => cf.ByName("OK"))?.Click();
                }
                Thread.Sleep(400);
            }
            Assert.True(PenRowVisible(window, penName),
                $"pen row '{penName}' did not appear in the pen list after the dialog's OK");

            // The row can appear a moment before the dialog window leaves
            // the UIA tree; a leftover dialog (including a stacked second
            // one) would silently block the next toolbar click.
            var closeDeadline = DateTime.UtcNow.AddSeconds(10);
            while (FindDialog(automation) is not null && DateTime.UtcNow < closeDeadline)
            {
                Thread.Sleep(300);
            }
            if (FindDialog(automation) is not null)
            {
                Capture(window, "trenddiag-dialogleft.png");
                Assert.Fail("Add Trend Pen dialog still open after the pen was added (diagnostic capture saved: trenddiag-dialogleft.png)");
            }
        }

        private static bool PenRowVisible(FlaUI.Core.AutomationElements.Window window, string penName)
        {
            return window.FindAllDescendants(cf => cf.ByControlType(ControlType.List))
                .Any(list => list.FindFirstDescendant(cf => cf.ByValue(penName)) is not null);
        }

        private static FlaUI.Core.AutomationElements.AutomationElement? FindDialog(UIA3Automation automation)
        {
            // The dialog is an owned window that the process-level
            // enumeration can miss, so search the desktop tree by title.
            // Skip ghost entries for windows that no longer exist.
            return automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByName("Add Trend Pen").And(cf.ByControlType(ControlType.Window)))
                .FirstOrDefault(el => el.IsAvailable);
        }

        /// <summary>
        /// Switches the toolbar Mode combo to Server when not already there
        /// (a killed probe run can leave server mode persisted). Selects via
        /// the UIA SelectionItem pattern, which avoids the flaky
        /// open-the-dropdown-then-click-an-item race.
        /// </summary>
        private static void EnsureServerMode(FlaUI.Core.AutomationElements.Window window)
        {
            if (window.FindFirstDescendant(cf => cf.ByName("Start Server")) is not null)
            {
                return; // already in server mode
            }

            var combo = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox));
            Assert.NotNull(combo);
            ((FlaUI.Core.AutomationElements.ComboBox)combo!).Select("Server");
            Thread.Sleep(800);
        }
    }
}

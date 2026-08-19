using System;
using System.IO;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

namespace ModbusForge.Tests.UITests
{
    /// <summary>
    /// Manual UI probes that launch the real app. Excluded from the standard headless
    /// test runs by the "FullyQualifiedName!~UITests" filter; run one explicitly, e.g.:
    ///
    /// dotnet test ModbusForge.Tests --filter "FullyQualifiedName~NodeEditorPinLabelUITests"
    ///
    /// The probe terminates the app afterwards (best effort) and drops a PNG capture
    /// next to the test binaries for visual inspection.
    /// </summary>
    public class NodeEditorPinLabelUITests
    {
        [Fact]
        public void Demo_Nodes_RenderPinLabels()
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

                // Open the Simulation tab (the visual node editor) and load the demo program.
                var simulationTab = window.FindFirstDescendant(cf => cf.ByName("Simulation"));
                Assert.NotNull(simulationTab);
                simulationTab!.Click();

                var demoButton = window.FindFirstDescendant(cf => cf.ByName("Demo"));
                Assert.NotNull(demoButton);
                demoButton.Click();

                // Give the demo graph a moment to attach, then capture the window.
                Thread.Sleep(2500);

                var capturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "pinlabels-capture.png"));
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

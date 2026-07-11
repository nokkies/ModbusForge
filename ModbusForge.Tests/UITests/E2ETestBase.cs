using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using UI.TestFramework;
using Xunit;
using AventStack.ExtentReports;
using System.Runtime.InteropServices;

namespace ModbusForge.Tests.UITests;

public class E2ETestBase : IDisposable
{
    protected static TestFrameworkManager FrameworkManager { get; }
    protected AutomationBase? Automation { get; }
    protected Application? App { get; private set; }
    protected FlaUI.Core.AutomationElements.Window? MainWindow { get; private set; }

    static E2ETestBase()
    {
        FrameworkManager = new TestFrameworkManager("E2ETestResults");
    }

    public E2ETestBase()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                Automation = new UIA3Automation();
                var binDir = AppDomain.CurrentDomain.BaseDirectory;
                // Path to the WPF executable. Tests run in ModbusForge.Tests/bin/Debug/net8.0-windows
                // so we look for ModbusForge.exe in the current directory or parent directories.
                var appPath = Path.Combine(binDir, "ModbusForge.exe");

                // Fallback to searching up tree if not in bin
                if (!File.Exists(appPath))
                {
                    appPath = Path.Combine(binDir, "..", "..", "..", "..", "ModbusForge", "bin", "Debug", "net8.0-windows", "ModbusForge.exe");
                }

                if (File.Exists(appPath))
                {
                    App = Application.Launch(appPath);
                    MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(10));
                }
            }
            catch(Exception)
            {
                // In headless linux / no GUI environments, FlaUI fails.
                // We keep properties null to mock tests.
            }
        }
    }

    protected AutomationElement WaitForElementByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        if (MainWindow == null)
            throw new InvalidOperationException("Main window is not available.");

        var expiresAt = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < expiresAt)
        {
            var element = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element != null)
                return element;
            Thread.Sleep(100);
        }

        throw new TimeoutException($"Element with automation ID '{automationId}' was not found.");
    }

    protected void OpenSimulation()
    {
        if (MainWindow == null)
            throw new InvalidOperationException("Main window is not available.");

        var simulationNavItem = WaitForElementByAutomationId("SimulationNavItem");
        var expiresAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < expiresAt)
        {
            simulationNavItem.Click();
            var paletteSearchBox = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PaletteSearchBox"));
            if (paletteSearchBox != null)
                return;
            Thread.Sleep(250);
        }

        throw new TimeoutException("Simulation content did not become active.");
    }

    protected void ReportResult(string testName, string description, Action testAction)
    {
        var testReport = FrameworkManager.CreateTest(testName, description);
        using var recorder = FrameworkManager.StartVideoRecording(testName);

        bool isSuccess = false;
        string message = "";

        try
        {
            testAction();
            isSuccess = true;
            message = "Test passed successfully.";
            testReport.Pass(message);
        }
        catch (Exception ex)
        {
            isSuccess = false;
            message = ex.Message;
            testReport.Fail(ex);
            throw;
        }
        finally
        {
            recorder.Stop();
            var screenshotPath = FrameworkManager.TakeScreenshot(testName);
            var safeName = string.Join("_", testName.Split(Path.GetInvalidFileNameChars()));
            var videoPath = Path.Combine(FrameworkManager.ReportsDirectory, $"{safeName}.avi");

            if (!string.IsNullOrEmpty(screenshotPath) && File.Exists(screenshotPath))
            {
                testReport.AddScreenCaptureFromPath(screenshotPath);
            }

            FrameworkManager.RecordResult(testName, isSuccess, message, screenshotPath, videoPath);
        }
    }

    public virtual void Dispose()
    {
        try
        {
            App?.Close();
        }
        catch { }
        finally
        {
            App?.Dispose();
            Automation?.Dispose();
            FrameworkManager.GenerateReports();
        }
    }
}

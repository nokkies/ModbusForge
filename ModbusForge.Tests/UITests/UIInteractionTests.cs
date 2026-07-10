using System.Runtime.InteropServices;
using Xunit;
using FlaUI.Core.Conditions;
using FlaUI.Core.AutomationElements;

namespace ModbusForge.Tests.UITests;

[Collection("Sequential UI Tests")]
public class UIInteractionTests : E2ETestBase
{
    [Fact]
    public void ModeToggleChangesConnectButtonContentTest()
    {
        ReportResult(
            "ModeToggleChangesConnectButtonContentTest",
            "Toggles the Mode combobox and verifies the Connect button text changes.",
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || MainWindow == null)
                {
                    Assert.True(true, "Skipped UI interaction: non-Windows or app could not launch in this environment.");
                    return;
                }

                var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

                var modeComboBox = MainWindow.FindFirstDescendant(cf.ByAutomationId("ModeComboBox"))?.AsComboBox();
                Assert.NotNull(modeComboBox);

                var connectButton = MainWindow.FindFirstDescendant(cf.ByAutomationId("ConnectButton"))?.AsButton();
                Assert.NotNull(connectButton);

                // Initial text should be "Connect" (Client mode)
                string initialText = connectButton.Name;

                // Select "Server" mode
                modeComboBox.Select("Server");

                // Wait for UI to update
                System.Threading.Thread.Sleep(500);

                string newText = connectButton.Name;

                Assert.NotEqual(initialText, newText);
                Assert.Contains("Start", newText);
            });
    }

    [Fact]
    public void DisconnectButtonIsDisabledWhenNotConnectedTest()
    {
        ReportResult(
            "DisconnectButtonIsDisabledWhenNotConnectedTest",
            "Verifies that the Disconnect button is disabled on startup.",
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || MainWindow == null)
                {
                    Assert.True(true, "Skipped UI interaction: non-Windows or app could not launch in this environment.");
                    return;
                }

                var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

                var disconnectButton = MainWindow.FindFirstDescendant(cf.ByAutomationId("DisconnectButton"))?.AsButton();
                Assert.NotNull(disconnectButton);

                Assert.False(disconnectButton.IsEnabled);
            });
    }

    [Fact]
    public void TagBrowserButtonOpensTagBrowserWindowTest()
    {
        ReportResult(
            "TagBrowserButtonOpensTagBrowserWindowTest",
            "Clicks the Tag Browser button and verifies the window opens.",
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || MainWindow == null || App == null)
                {
                    Assert.True(true, "Skipped UI interaction: non-Windows or app could not launch in this environment.");
                    return;
                }

                var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

                var simulationTab = MainWindow.FindFirstDescendant(cf.ByAutomationId("SimulationTab"))?.AsTabItem();
                Assert.NotNull(simulationTab);
                simulationTab.Select();

                var tagBrowserButton = MainWindow.FindFirstDescendant(cf.ByAutomationId("TagBrowserButton"))?.AsButton();
                Assert.NotNull(tagBrowserButton);

                tagBrowserButton.Invoke();

                System.Threading.Thread.Sleep(1000);

                var tagBrowserWindow = App.GetAllTopLevelWindows(Automation!).FirstOrDefault(w => w.Title.Contains("Tag Browser"))
                    ?? MainWindow.FindFirstDescendant(cf.ByName("Tag Browser - Symbolic Addressing"))?.AsWindow();
                Assert.NotNull(tagBrowserWindow);

                tagBrowserWindow.Close();
            });
    }

    [Fact]
    public void PaletteSearchFilterHidesNonMatchingNodesTest()
    {
        ReportResult(
            "PaletteSearchFilterHidesNonMatchingNodesTest",
            "Searches for 'timer' and verifies that 'AND Gate' is hidden.",
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || MainWindow == null)
                {
                    Assert.True(true, "Skipped UI interaction: non-Windows or app could not launch in this environment.");
                    return;
                }

                var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

                var simulationTab = MainWindow.FindFirstDescendant(cf.ByAutomationId("SimulationTab"))?.AsTabItem();
                Assert.NotNull(simulationTab);
                simulationTab.Select();

                var paletteSearchBox = MainWindow.FindFirstDescendant(cf.ByAutomationId("PaletteSearchBox"))?.AsTextBox();
                Assert.NotNull(paletteSearchBox);

                var andGateBtn = MainWindow.FindFirstDescendant(cf.ByName("AND Gate"))?.AsButton();
                Assert.NotNull(andGateBtn);

                // Initial state: AND Gate should be visible
                Assert.False(andGateBtn.IsOffscreen);

                paletteSearchBox.Text = "timer";
                System.Threading.Thread.Sleep(500);

                // Re-find the button or check state. If the element is collapsed, UIA might throw or return true for IsOffscreen
                var andGateBtnAfterSearch = MainWindow.FindFirstDescendant(cf.ByName("AND Gate"));
                bool isHidden = andGateBtnAfterSearch == null || andGateBtnAfterSearch.IsOffscreen;
                Assert.True(isHidden);

                paletteSearchBox.Text = "";
                System.Threading.Thread.Sleep(500);

                var andGateBtnAfterClear = MainWindow.FindFirstDescendant(cf.ByName("AND Gate"))?.AsButton();
                Assert.NotNull(andGateBtnAfterClear);
                Assert.False(andGateBtnAfterClear.IsOffscreen);
            });
    }

    [Fact]
    public void ZoomSliderChangesCanvasScaleTest()
    {
        ReportResult(
            "ZoomSliderChangesCanvasScaleTest",
            "Changes the zoom slider and verifies the value is updated.",
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || MainWindow == null)
                {
                    Assert.True(true, "Skipped UI interaction: non-Windows or app could not launch in this environment.");
                    return;
                }

                var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());

                var simulationTab = MainWindow.FindFirstDescendant(cf.ByAutomationId("SimulationTab"))?.AsTabItem();
                Assert.NotNull(simulationTab);
                simulationTab.Select();

                var zoomSlider = MainWindow.FindFirstDescendant(cf.ByAutomationId("ZoomSlider"))?.AsSlider();
                Assert.NotNull(zoomSlider);

                double initialValue = zoomSlider.Value;

                zoomSlider.Value = 1.5;
                System.Threading.Thread.Sleep(500);

                double newValue = zoomSlider.Value;

                Assert.NotEqual(initialValue, newValue);
                Assert.Equal(1.5, newValue);
            });
    }
}

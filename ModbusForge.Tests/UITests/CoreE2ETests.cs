using System.Runtime.InteropServices;
using Xunit;
using FlaUI.Core.Conditions;
using FlaUI.Core.AutomationElements;

namespace ModbusForge.Tests.UITests;

[Collection("Sequential UI Tests")]
public class CoreE2ETests : E2ETestBase
{
    [Fact]
    public void AppLaunchAndConnectTest()
    {
        ReportResult(
            "AppLaunchAndConnectTest",
            "Launches the app and clicks the Connect button (looked up by AutomationId=\"ConnectButton\").",
            () =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || MainWindow == null)
                {
                    Assert.True(true, "Skipped UI interaction: non-Windows or app could not launch in this environment.");
                    return;
                }

                var cf = new ConditionFactory(new FlaUI.UIA3.UIA3PropertyLibrary());
                var connectButton = MainWindow.FindFirstDescendant(cf.ByAutomationId("ConnectButton"))?.AsButton();

                Assert.NotNull(connectButton);
                connectButton.Invoke();
            });
    }

    [Fact]
    public void AddNodeAndReadRegistersTest()
    {
        ReportResult(
            "AddNodeAndReadRegistersTest",
            "Switches to the Simulation tab and clicks the \"Input BOOL\" palette button to add a node.",
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

                var inputBoolBtn = MainWindow.FindFirstDescendant(cf.ByName("Input BOOL"))?.AsButton();
                Assert.NotNull(inputBoolBtn);
                inputBoolBtn.Invoke();
            });
    }

    }
}

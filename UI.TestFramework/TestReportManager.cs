using ClosedXML.Excel;
using AventStack.ExtentReports;
using AventStack.ExtentReports.Reporter;

namespace UI.TestFramework;

public class TestReportManager
{
    private readonly ExtentReports _extent;
    private readonly string _reportDir;
    private readonly string _excelPath;

    public TestReportManager(string reportDir)
    {
        _reportDir = reportDir;
        Directory.CreateDirectory(_reportDir);

        _excelPath = Path.Combine(_reportDir, "TestReport.xlsx");

        var htmlReporter = new AventStack.ExtentReports.Reporter.ExtentSparkReporter(Path.Combine(_reportDir, "TestReport.html"));
        _extent = new ExtentReports();
        _extent.AttachReporter(htmlReporter);
    }

    public ExtentTest CreateTest(string testName, string description)
    {
        return _extent.CreateTest(testName, description);
    }

    public void Flush()
    {
        _extent.Flush();
    }
}

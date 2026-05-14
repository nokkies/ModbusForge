using AventStack.ExtentReports;
using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.Capturing;
using System.Runtime.InteropServices;

namespace UI.TestFramework;

public class TestFrameworkManager : IDisposable
{
    private readonly string _reportsDirectory;
    private readonly TestReportManager _htmlReporter;
    private readonly ExcelReporter _excelReporter;

    public TestFrameworkManager(string outputDirectory = "TestResults")
    {
        _reportsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_reportsDirectory);

        _htmlReporter = new TestReportManager(_reportsDirectory);
        _excelReporter = new ExcelReporter(Path.Combine(_reportsDirectory, "TestReport.xlsx"));
    }

    public string ReportsDirectory => _reportsDirectory;

    public ExtentTest CreateTest(string testName, string description)
    {
        return _htmlReporter.CreateTest(testName, description);
    }

    public VideoRecorder StartVideoRecording(string testName)
    {
        var safeName = string.Join("_", testName.Split(Path.GetInvalidFileNameChars()));
        var videoPath = Path.Combine(_reportsDirectory, $"{safeName}.avi");
        var recorder = new VideoRecorder(videoPath);
        recorder.Start();
        return recorder;
    }

    public string TakeScreenshot(string testName)
    {
        var safeName = string.Join("_", testName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(_reportsDirectory, $"{safeName}_{Guid.NewGuid()}.png");

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var image = Capture.MainScreen();
                image.ToFile(path);
            }
            else
            {
                // Fallback for non-windows / headless devboxes
                File.WriteAllText(path, "Simulated Screenshot on non-Windows environment");
            }
        }
        catch(Exception)
        {
            File.WriteAllText(path, "Screenshot capture failed");
        }

        return path;
    }

    public void RecordResult(string testName, bool isSuccess, string message, string screenshotPath, string videoPath)
    {
        _excelReporter.AddResult(testName, isSuccess ? "Pass" : "Fail", message, screenshotPath, videoPath);
    }

    public void GenerateReports()
    {
        _htmlReporter.Flush();
        _excelReporter.Generate();
    }

    public void Dispose()
    {
        GenerateReports();
    }
}

using ClosedXML.Excel;
using System.Collections.Concurrent;

namespace UI.TestFramework;

public class ExcelReporter
{
    private readonly string _filePath;
    private readonly ConcurrentBag<TestResultRow> _results = new();

    public ExcelReporter(string filePath)
    {
        _filePath = filePath;
    }

    public void AddResult(string testName, string status, string message, string screenshotPath, string videoPath)
    {
        _results.Add(new TestResultRow
        {
            TestName = testName,
            Status = status,
            Message = message,
            ScreenshotPath = screenshotPath,
            VideoPath = videoPath,
            Timestamp = DateTime.Now
        });
    }

    public void Generate()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Test Results");

        worksheet.Cell(1, 1).Value = "Test Name";
        worksheet.Cell(1, 2).Value = "Status";
        worksheet.Cell(1, 3).Value = "Message";
        worksheet.Cell(1, 4).Value = "Timestamp";
        worksheet.Cell(1, 5).Value = "Screenshot";
        worksheet.Cell(1, 6).Value = "Video";

        var headerRow = worksheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var result in _results.OrderBy(r => r.Timestamp))
        {
            worksheet.Cell(row, 1).Value = result.TestName;

            var statusCell = worksheet.Cell(row, 2);
            statusCell.Value = result.Status;
            statusCell.Style.Font.FontColor = result.Status == "Pass" ? XLColor.Green : XLColor.Red;

            worksheet.Cell(row, 3).Value = result.Message;
            worksheet.Cell(row, 4).Value = result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            worksheet.Cell(row, 5).Value = result.ScreenshotPath;
            worksheet.Cell(row, 6).Value = result.VideoPath;

            row++;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(_filePath);
    }

    private class TestResultRow
    {
        public string TestName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
        public string ScreenshotPath { get; set; } = "";
        public string VideoPath { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}

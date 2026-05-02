using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using Moq;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class ImportCsvTests
    {
        [Fact]
        public async Task ImportCsvAsync_ShouldParseAndPublishData()
        {
            // Arrange
            var mockLoggerSvc = new Mock<ITrendLogger>();
            var mockFileDialogService = new Mock<IFileDialogService>();
            var options = Options.Create(new LoggingSettings());

            var viewModel = new TrendViewModel(mockLoggerSvc.Object, options, mockFileDialogService.Object);

            string tempFile = Path.GetTempFileName() + ".csv";
            try
            {
                await File.WriteAllLinesAsync(tempFile, new[]
                {
                    "series,timestamp_utc,value",
                    "Series1,2023-10-27T10:00:00Z,123.45",
                    "Series1,2023-10-27T10:01:00Z,678.9"
                });

                string expectedKey = $"Imported:{Path.GetFileNameWithoutExtension(tempFile)}";

                // Act
                await viewModel.ImportCsvAsync(tempFile);

                // Assert
                mockLoggerSvc.Verify(l => l.Add(expectedKey, expectedKey), Times.Once);
                mockLoggerSvc.Verify(l => l.Publish(expectedKey, 123.45, It.IsAny<DateTime>()), Times.Once);
                mockLoggerSvc.Verify(l => l.Publish(expectedKey, 678.9, It.IsAny<DateTime>()), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ImportCsvAsync_ShouldSkipInvalidLines()
        {
            // Arrange
            var mockLoggerSvc = new Mock<ITrendLogger>();
            var mockFileDialogService = new Mock<IFileDialogService>();
            var options = Options.Create(new LoggingSettings());

            var viewModel = new TrendViewModel(mockLoggerSvc.Object, options, mockFileDialogService.Object);

            string tempFile = Path.GetTempFileName() + ".csv";
            try
            {
                await File.WriteAllLinesAsync(tempFile, new[]
                {
                    "series,timestamp_utc,value",
                    "Series1,invalid-date,123.45",
                    "Series1,2023-10-27T10:00:00Z,invalid-value",
                    "Series1,2023-10-27T10:01:00Z,678.9"
                });

                string expectedKey = $"Imported:{Path.GetFileNameWithoutExtension(tempFile)}";

                // Act
                await viewModel.ImportCsvAsync(tempFile);

                // Assert
                mockLoggerSvc.Verify(l => l.Publish(expectedKey, It.IsAny<double>(), It.IsAny<DateTime>()), Times.Exactly(1));
                mockLoggerSvc.Verify(l => l.Publish(expectedKey, 678.9, It.IsAny<DateTime>()), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}

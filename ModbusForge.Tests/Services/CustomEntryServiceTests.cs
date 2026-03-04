using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class CustomEntryServiceTests
    {
        private readonly Mock<IFileDialogService> _mockFileDialogService;
        private readonly Mock<ILogger<CustomEntryService>> _mockLogger;
        private readonly CustomEntryService _service;

        public CustomEntryServiceTests()
        {
            _mockFileDialogService = new Mock<IFileDialogService>();
            _mockLogger = new Mock<ILogger<CustomEntryService>>();
            _service = new CustomEntryService(_mockFileDialogService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task LoadCustomAsync_ReturnsNull_WhenUserCancels()
        {
            // Arrange
            _mockFileDialogService.Setup(s => s.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string)null);

            // Act
            var result = await _service.LoadCustomAsync();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task LoadCustomAsync_ThrowsInvalidDataException_WhenFileTooLarge()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            try
            {
                // Create a file larger than 2MB
                using (var fs = new FileStream(tempFile, FileMode.OpenOrCreate))
                {
                    fs.SetLength(3 * 1024 * 1024); // 3MB
                }

                _mockFileDialogService.Setup(s => s.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(tempFile);

                // Act & Assert
                var exception = await Assert.ThrowsAsync<InvalidDataException>(() => _service.LoadCustomAsync());
                Assert.Contains("too large", exception.Message);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task LoadCustomAsync_LoadsValidFile_Successfully()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var entries = new[]
            {
                new { Name = "Test", Address = 1, Type = "uint", Value = "100", Area = "HoldingRegister" }
            };
            var json = JsonSerializer.Serialize(entries);
            await File.WriteAllTextAsync(tempFile, json);

            try
            {
                _mockFileDialogService.Setup(s => s.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(tempFile);

                // Act
                var result = await _service.LoadCustomAsync();

                // Assert
                Assert.NotNull(result);
                Assert.Single(result);
                Assert.Equal("Test", result[0].Name);
                Assert.Equal(1, result[0].Address);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SaveCustomAsync_SavesFile_Successfully()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var entries = new ObservableCollection<CustomEntry>
            {
                new CustomEntry { Name = "SaveTest", Address = 10, Value = "50" }
            };

            _mockFileDialogService.Setup(s => s.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(tempFile);

            try
            {
                // Act
                await _service.SaveCustomAsync(entries);

                // Assert
                Assert.True(File.Exists(tempFile));
                var json = await File.ReadAllTextAsync(tempFile);
                var doc = JsonDocument.Parse(json);
                Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
                Assert.Equal("SaveTest", doc.RootElement[0].GetProperty("Name").GetString());
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}

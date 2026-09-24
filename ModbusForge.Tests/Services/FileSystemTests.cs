using System;
using System.IO;
using System.Threading.Tasks;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class FileSystemTests : IDisposable
    {
        private readonly string _tempDirectory;

        public FileSystemTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch
                {
                    // Ignore errors during test cleanup
                }
            }
        }

        [Fact]
        public async Task ReadAllTextAsync_WhenFileExists_ReturnsFileContent()
        {
            // Arrange
            var fileSystem = new FileSystem();
            var filePath = Path.Combine(_tempDirectory, "test_read.txt");
            var expectedContent = "Hello, ModbusForge FileSystem Test!";
            await File.WriteAllTextAsync(filePath, expectedContent);

            // Act
            var actualContent = await fileSystem.ReadAllTextAsync(filePath);

            // Assert
            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        public async Task ReadAllTextAsync_WhenFileDoesNotExist_ThrowsFileNotFoundException()
        {
            // Arrange
            var fileSystem = new FileSystem();
            var filePath = Path.Combine(_tempDirectory, "non_existent_file.txt");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => fileSystem.ReadAllTextAsync(filePath));
        }

        [Fact]
        public async Task WriteAllTextAsync_WhenCalled_WritesContentToFile()
        {
            // Arrange
            var fileSystem = new FileSystem();
            var filePath = Path.Combine(_tempDirectory, "test_write.txt");
            var expectedContent = "Testing WriteAllTextAsync content.";

            // Act
            await fileSystem.WriteAllTextAsync(filePath, expectedContent);

            // Assert
            Assert.True(File.Exists(filePath));
            var actualContent = await File.ReadAllTextAsync(filePath);
            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        public void FileExists_WhenFileExists_ReturnsTrue()
        {
            // Arrange
            var fileSystem = new FileSystem();
            var filePath = Path.Combine(_tempDirectory, "existing_file.txt");
            File.WriteAllText(filePath, "exists");

            // Act
            var result = fileSystem.FileExists(filePath);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void FileExists_WhenFileDoesNotExist_ReturnsFalse()
        {
            // Arrange
            var fileSystem = new FileSystem();
            var filePath = Path.Combine(_tempDirectory, "missing_file.txt");

            // Act
            var result = fileSystem.FileExists(filePath);

            // Assert
            Assert.False(result);
        }
    }
}

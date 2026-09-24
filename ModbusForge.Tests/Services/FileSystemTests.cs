using System;
using System.IO;
using System.Threading.Tasks;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class FileSystemTests : IDisposable
    {
        private readonly FileSystem _fileSystem;
        private readonly string _tempDirectory;

        public FileSystemTests()
        {
            _fileSystem = new FileSystem();
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString());
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
                    // Ignore directory deletion errors on test cleanup
                }
            }
        }

        [Fact]
        public async Task WriteAllTextAsync_ValidPathAndContent_WritesFileSuccessfully()
        {
            // Arrange
            string filePath = Path.Combine(_tempDirectory, "write_test.txt");
            string expectedContent = "Hello ModbusForge FileSystem";

            // Act
            await _fileSystem.WriteAllTextAsync(filePath, expectedContent);

            // Assert
            Assert.True(File.Exists(filePath));
            string actualContent = await File.ReadAllTextAsync(filePath);
            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        public async Task WriteAllTextAsync_ExistingFile_OverwritesContent()
        {
            // Arrange
            string filePath = Path.Combine(_tempDirectory, "overwrite_test.txt");
            await File.WriteAllTextAsync(filePath, "Initial Content");

            string newContent = "Overwritten Content";

            // Act
            await _fileSystem.WriteAllTextAsync(filePath, newContent);

            // Assert
            string actualContent = await File.ReadAllTextAsync(filePath);
            Assert.Equal(newContent, actualContent);
        }

        [Fact]
        public async Task ReadAllTextAsync_ExistingFile_ReturnsCorrectContent()
        {
            // Arrange
            string filePath = Path.Combine(_tempDirectory, "read_test.txt");
            string expectedContent = "Content to read back";
            await File.WriteAllTextAsync(filePath, expectedContent);

            // Act
            string actualContent = await _fileSystem.ReadAllTextAsync(filePath);

            // Assert
            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        public async Task ReadAllTextAsync_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            string nonExistentPath = Path.Combine(_tempDirectory, "non_existent_file.txt");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => _fileSystem.ReadAllTextAsync(nonExistentPath));
        }

        [Fact]
        public async Task FileExists_ExistingFile_ReturnsTrue()
        {
            // Arrange
            string filePath = Path.Combine(_tempDirectory, "exists_test.txt");
            await File.WriteAllTextAsync(filePath, "test");

            // Act
            bool exists = _fileSystem.FileExists(filePath);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void FileExists_NonExistentFile_ReturnsFalse()
        {
            // Arrange
            string nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist.txt");

            // Act
            bool exists = _fileSystem.FileExists(nonExistentPath);

            // Assert
            Assert.False(exists);
        }
    }
}

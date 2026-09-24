using System;
using System.IO;
using System.Threading.Tasks;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.Services
{
    public class FileSystemTests : IDisposable
    {
        private readonly string _tempDirectory;

        public FileSystemTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FileSystemTests_" + Guid.NewGuid().ToString("N"));
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
        public void FileExists_ExistingFile_ReturnsTrue()
        {
            var fileSystem = new FileSystem();
            string filePath = Path.Combine(_tempDirectory, "test_exists.txt");
            File.WriteAllText(filePath, "sample content");

            bool exists = fileSystem.FileExists(filePath);

            Assert.True(exists);
        }

        [Fact]
        public void FileExists_NonExistingFile_ReturnsFalse()
        {
            var fileSystem = new FileSystem();
            string filePath = Path.Combine(_tempDirectory, "non_existent.txt");

            bool exists = fileSystem.FileExists(filePath);

            Assert.False(exists);
        }

        [Fact]
        public async Task WriteAllTextAsync_ValidPath_WritesContentToFile()
        {
            var fileSystem = new FileSystem();
            string filePath = Path.Combine(_tempDirectory, "test_write.txt");
            string expectedContent = "Hello, ModbusForge FileSystem!";

            await fileSystem.WriteAllTextAsync(filePath, expectedContent);

            Assert.True(File.Exists(filePath));
            string actualContent = await File.ReadAllTextAsync(filePath);
            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        public async Task WriteAllTextAsync_OverwriteExistingFile_UpdatesContent()
        {
            var fileSystem = new FileSystem();
            string filePath = Path.Combine(_tempDirectory, "test_overwrite.txt");
            await File.WriteAllTextAsync(filePath, "Initial Content");

            string newContent = "Updated Content";
            await fileSystem.WriteAllTextAsync(filePath, newContent);

            string actualContent = await File.ReadAllTextAsync(filePath);
            Assert.Equal(newContent, actualContent);
        }

        [Fact]
        public async Task ReadAllTextAsync_ExistingFile_ReadsContentCorrectly()
        {
            var fileSystem = new FileSystem();
            string filePath = Path.Combine(_tempDirectory, "test_read.txt");
            string expectedContent = "Content to read back asynchronously";
            await File.WriteAllTextAsync(filePath, expectedContent);

            string actualContent = await fileSystem.ReadAllTextAsync(filePath);

            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        public async Task ReadAllTextAsync_NonExistingFile_ThrowsFileNotFoundException()
        {
            var fileSystem = new FileSystem();
            string filePath = Path.Combine(_tempDirectory, "does_not_exist.txt");

            await Assert.ThrowsAsync<FileNotFoundException>(() => fileSystem.ReadAllTextAsync(filePath));
        }
    }
}

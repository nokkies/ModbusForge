using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Coordinators
{
    public class ConfigurationCoordinatorTests
    {
        private class TestFileSystem : IFileSystem
        {
            public Dictionary<string, string> Files { get; } = new();

            public Task<string> ReadAllTextAsync(string path)
            {
                if (Files.TryGetValue(path, out var content))
                    return Task.FromResult(content);
                throw new FileNotFoundException($"File not found: {path}");
            }

            public Task WriteAllTextAsync(string path, string contents)
            {
                Files[path] = contents;
                return Task.CompletedTask;
            }

            public bool FileExists(string path) => Files.ContainsKey(path);
        }

        private class TestFileDialogService : IFileDialogService
        {
            public string? SaveResult { get; set; }
            public string? OpenResult { get; set; }

            public string? ShowSaveFileDialog(string title, string filter, string defaultFileName) => SaveResult;
            public string? ShowOpenFileDialog(string title, string filter) => OpenResult;
        }

        private class TestInputDialogService : IInputDialogService
        {
            public bool Result { get; set; }
            public string Input { get; set; } = string.Empty;

            public bool TryGetInput(string title, string prompt, string defaultValue, out string input)
            {
                input = Input;
                return Result;
            }
        }

        private static ConfigurationCoordinator CreateCoordinator(
            IFileDialogService? fileDialog = null,
            IFileSystem? fileSystem = null,
            IInputDialogService? inputDialog = null)
        {
            return new ConfigurationCoordinator(
                new Mock<ILogger<ConfigurationCoordinator>>().Object,
                fileDialog,
                fileSystem,
                inputDialog,
                new Mock<IDialogService>().Object);
        }

        private static ProjectWorkspaceSnapshot CreateSnapshot()
        {
            var config = new UnitIdConfiguration(1);
            config.CustomEntries.Add(new CustomEntry { Address = 1, Area = "HoldingRegister", Type = "uint", Value = "42" });

            return new ProjectWorkspaceSnapshot
            {
                Mode = "Server",
                ServerAddress = "127.0.0.1",
                Port = 502,
                ServerUnitId = "1",
                ClientUnitId = 1,
                SelectedUnitId = 1,
                IsServerMode = true,
                UnitConfigurations = { [1] = config },
                VisibleTabs = new List<string> { "Registers", "CustomWatch" }
            };
        }

        [Fact]
        public async Task SaveProjectAsync_RoundTrip_WithSnapshot()
        {
            // Arrange
            var fileSystem = new TestFileSystem();
            var fileDialog = new TestFileDialogService { SaveResult = "roundtrip.mfp" };
            var coordinator = CreateCoordinator(fileDialog, fileSystem);
            var snapshot = CreateSnapshot();

            // Act - save
            var saveResult = await coordinator.SaveProjectAsync(snapshot);

            // Assert save
            Assert.True(saveResult.Success);
            Assert.True(fileSystem.Files.ContainsKey("roundtrip.mfp"));

            // Act - load
            fileDialog.OpenResult = "roundtrip.mfp";
            var loadResult = await coordinator.LoadProjectAsync();

            // Assert load
            Assert.True(loadResult.Success);
            Assert.NotNull(loadResult.Snapshot);
            Assert.Equal("Server", loadResult.Snapshot!.Mode);
            Assert.Equal(502, loadResult.Snapshot.Port);
            Assert.Equal("1", loadResult.Snapshot.ServerUnitId);
            Assert.Single(loadResult.Snapshot.UnitConfigurations);
            Assert.Equal("42", loadResult.Snapshot.UnitConfigurations[1].CustomEntries[0].Value);
            Assert.Contains("CustomWatch", loadResult.Snapshot.VisibleTabs);
        }

        [Fact]
        public async Task LoadProjectAsync_InvalidJson_ReturnsFailure()
        {
            // Arrange
            var fileSystem = new TestFileSystem();
            fileSystem.Files["invalid.mfp"] = "not valid json";
            var fileDialog = new TestFileDialogService { OpenResult = "invalid.mfp" };
            var coordinator = CreateCoordinator(fileDialog, fileSystem);

            // Act
            var result = await coordinator.LoadProjectAsync();

            // Assert
            Assert.False(result.Success);
            Assert.Null(result.Snapshot);
            Assert.Contains("Error loading project", result.Message);
        }

        [Fact]
        public async Task ImportUnitIdsAsync_ImportsOnlyNewConfigurations()
        {
            // Arrange
            var importedConfig = new UnitIdConfiguration(5);
            importedConfig.CustomEntries.Add(new CustomEntry { Address = 1, Value = "99" });

            var projectConfig = new ProjectConfiguration
            {
                GlobalSettings = new GlobalSettings { Mode = "Server", ServerAddress = "127.0.0.1", Port = 502 },
                UnitConfigurations = new Dictionary<byte, UnitIdConfiguration> { [5] = importedConfig }
            };

            var fileSystem = new TestFileSystem();
            fileSystem.Files["import.mfp"] = JsonSerializer.Serialize(projectConfig, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var fileDialog = new TestFileDialogService { OpenResult = "import.mfp" };
            var coordinator = CreateCoordinator(fileDialog, fileSystem);

            // Act
            var result = await coordinator.ImportUnitIdsAsync();

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Snapshot);
            Assert.Single(result.Snapshot!.UnitConfigurations);
            Assert.True(result.Snapshot.UnitConfigurations.ContainsKey(5));
        }

        [Fact]
        public async Task ExportUnitIdAsync_RejectsClientMode()
        {
            // Arrange
            var fileSystem = new TestFileSystem();
            var coordinator = CreateCoordinator(fileSystem: fileSystem);
            var snapshot = new ProjectWorkspaceSnapshot
            {
                Mode = "Client",
                IsServerMode = false,
                ServerAddress = "127.0.0.1",
                Port = 502
            };

            // Act
            var result = await coordinator.ExportUnitIdAsync(snapshot, 1);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Server mode", result.Message);
        }

        [Fact]
        public async Task ImportUnitIdAsAsync_PromptsAndReturnsImportedConfiguration()
        {
            // Arrange
            var importedConfig = new UnitIdConfiguration(3);
            importedConfig.CustomEntries.Add(new CustomEntry { Address = 1, Value = "77" });

            var projectConfig = new ProjectConfiguration
            {
                GlobalSettings = new GlobalSettings { Mode = "Server", ServerAddress = "127.0.0.1", Port = 502 },
                UnitConfigurations = new Dictionary<byte, UnitIdConfiguration> { [3] = importedConfig }
            };

            var fileSystem = new TestFileSystem();
            fileSystem.Files["unit.mui"] = JsonSerializer.Serialize(projectConfig, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var fileDialog = new TestFileDialogService { OpenResult = "unit.mui" };
            var inputDialog = new TestInputDialogService { Result = true, Input = "7" };
            var coordinator = CreateCoordinator(fileDialog, fileSystem, inputDialog);

            // Act
            var result = await coordinator.ImportUnitIdAsAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Equal((byte)7, result.ImportedUnitId!.Value);
            Assert.NotNull(result.ImportedConfiguration);
            Assert.Equal("77", result.ImportedConfiguration!.CustomEntries[0].Value);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using ModbusForge.ViewModels.Coordinators;
using Moq;
using Xunit;

namespace ModbusForge.Tests.ViewModels
{
    public class MainViewModelTests
    {
        private class TestDispatcher : IDispatcher
        {
            public void Invoke(Action action) => action();
            public T Invoke<T>(Func<T> func) => func();
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
            public Task<T> InvokeAsync<T>(Func<T> func) { var r = func(); return Task.FromResult(r); }
        }

        private class TestFileSystem : IFileSystem
        {
            public Dictionary<string, string> Files { get; } = new();

            public Task<string> ReadAllTextAsync(string path)
            {
                if (Files.TryGetValue(path, out var content))
                    return Task.FromResult(content);
                throw new System.IO.FileNotFoundException($"File not found: {path}");
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

        private MainViewModel CreateViewModel(ConfigurationCoordinator? configurationCoordinator = null, MonitoringCoordinator? monitoringCoordinator = null)
        {
            var mockTcpService = new Mock<ModbusTcpService>(new Mock<ILogger<ModbusTcpService>>().Object);
            var mockServerService = new Mock<ModbusServerService>(new Mock<ILogger<ModbusServerService>>().Object);
            var mockLogger = new Mock<ILogger<MainViewModel>>();
            var mockOptions = new Mock<IOptions<ServerSettings>>();
            mockOptions.Setup(o => o.Value).Returns(new ServerSettings());
            var mockTrendLogger = new Mock<ITrendLogger>();
            var mockCustomEntryService = new Mock<ICustomEntryService>();
            var mockConsoleLogger = new Mock<IConsoleLoggerService>();

            var mockRetry = new Mock<IRetryPolicyService>();
            mockRetry.Setup(r => r.ExecuteWithRetryAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(async (Func<Task<bool>> op, string name, int max, int init, int maxD) => await op());
            var mockValidation = new Mock<IValidationService>();
            mockValidation.Setup(v => v.ValidateIpAddress(It.IsAny<string>())).Returns(ValidationResult.Success);
            mockValidation.Setup(v => v.ValidatePort(It.IsAny<int>())).Returns(ValidationResult.Success);
            var mockError = new Mock<IErrorHandlingService>();
            mockError.Setup(e => e.HandleError(It.IsAny<Exception>(), It.IsAny<string>()))
                .Returns(new ErrorHandlingResult { UserMessage = "Error", RecoverySuggestion = "Suggestion" });
            var mockCircuit = new Mock<ICircuitBreakerService>();
            mockCircuit.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<CircuitBreakerConfig>()))
                .Returns(async (string name, Func<Task<bool>> op, CircuitBreakerConfig cfg) => await op());

            var connectionCoordinator = new ConnectionCoordinator(
                mockTcpService.Object,
                mockServerService.Object,
                mockConsoleLogger.Object,
                new Mock<ILogger<ConnectionCoordinator>>().Object,
                mockRetry.Object,
                mockValidation.Object,
                mockError.Object,
                mockCircuit.Object,
                new Mock<IDialogService>().Object);
            var registerCoordinator = new RegisterCoordinator(
                mockTcpService.Object,
                mockServerService.Object,
                mockConsoleLogger.Object,
                new Mock<ILogger<RegisterCoordinator>>().Object);
            var customEntryCoordinator = new CustomEntryCoordinator(
                registerCoordinator,
                mockCustomEntryService.Object,
                mockTcpService.Object,
                mockServerService.Object,
                new Mock<ILogger<CustomEntryCoordinator>>().Object);
            var trendCoordinator = new TrendCoordinator(
                mockTcpService.Object,
                mockServerService.Object,
                mockTrendLogger.Object,
                new Mock<ILogger<TrendCoordinator>>().Object,
                new Mock<ISettingsService>().Object);

            var monitoringCoordinatorInstance = monitoringCoordinator ?? new MonitoringCoordinator(
                Mock.Of<IMonitoringCallbacks>(),
                Mock.Of<IPeriodicScheduler>(),
                Mock.Of<IPeriodicScheduler>(),
                Mock.Of<IPeriodicScheduler>(),
                new Mock<ILogger<MonitoringCoordinator>>().Object);

            var unitConfigurationStore = new UnitConfigurationStore(new TestDispatcher());

            return new MainViewModel(
                mockTcpService.Object,
                mockServerService.Object,
                mockLogger.Object,
                mockOptions.Object,
                mockTrendLogger.Object,
                mockCustomEntryService.Object,
                mockConsoleLogger.Object,
                connectionCoordinator,
                registerCoordinator,
                customEntryCoordinator,
                trendCoordinator,
                configurationCoordinator ?? new ConfigurationCoordinator(new Mock<ILogger<ConfigurationCoordinator>>().Object),
                monitoringCoordinatorInstance,
                unitConfigurationStore,
                null,
                new VisualNodeEditorViewModel(),
                new TestDispatcher());
        }

        [Fact]
        public void Constructor_AcceptsAllDependencies()
        {
            // Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.NotNull(viewModel);
            Assert.NotNull(viewModel.CustomEntries);
            Assert.Equal("Client", viewModel.Mode);
        }

        [Fact]
        public async Task LoadProjectAsync_InvalidJson_DoesNotMutateState()
        {
            // Arrange
            var fileSystem = new TestFileSystem();
            fileSystem.Files["bad-project.mfp"] = "this is not valid json";
            var fileDialog = new TestFileDialogService { OpenResult = "bad-project.mfp" };
            var configurationCoordinator = new ConfigurationCoordinator(
                new Mock<ILogger<ConfigurationCoordinator>>().Object,
                fileDialog,
                fileSystem,
                new Mock<IInputDialogService>().Object,
                new Mock<IDialogService>().Object);

            var viewModel = CreateViewModel(configurationCoordinator);
            var originalMode = viewModel.Mode;
            var originalServerAddress = viewModel.ServerAddress;
            var originalUnitConfigCount = viewModel.UnitConfigurations.Count;

            var method = typeof(MainViewModel).GetMethod("LoadProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            // Act
            await (Task)method!.Invoke(viewModel, null)!;

            // Assert
            Assert.Equal(originalMode, viewModel.Mode);
            Assert.Equal(originalServerAddress, viewModel.ServerAddress);
            Assert.Equal(originalUnitConfigCount, viewModel.UnitConfigurations.Count);
            Assert.Contains("Error loading project", viewModel.StatusMessage);
        }

        [Fact]
        public async Task LoadProjectAsync_CancelledDialog_DoesNotMutateState()
        {
            // Arrange
            var fileDialog = new TestFileDialogService { OpenResult = null };
            var configurationCoordinator = new ConfigurationCoordinator(
                new Mock<ILogger<ConfigurationCoordinator>>().Object,
                fileDialog,
                new TestFileSystem(),
                new Mock<IInputDialogService>().Object,
                new Mock<IDialogService>().Object);

            var viewModel = CreateViewModel(configurationCoordinator);
            var originalMode = viewModel.Mode;

            var method = typeof(MainViewModel).GetMethod("LoadProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            await (Task)method!.Invoke(viewModel, null)!;

            // Assert
            Assert.Equal(originalMode, viewModel.Mode);
            Assert.Equal("Load cancelled", viewModel.StatusMessage);
        }

        [Fact]
        public async Task SaveProjectAsync_DelegatesToCoordinator()
        {
            // Arrange
            var fileSystem = new TestFileSystem();
            var fileDialog = new TestFileDialogService { SaveResult = "project.mfp" };
            var configurationCoordinator = new ConfigurationCoordinator(
                new Mock<ILogger<ConfigurationCoordinator>>().Object,
                fileDialog,
                fileSystem,
                new Mock<IInputDialogService>().Object,
                new Mock<IDialogService>().Object);

            var viewModel = CreateViewModel(configurationCoordinator);
            viewModel.CustomEntries.Add(new CustomEntry { Address = 1, Area = "HoldingRegister", Type = "uint", Value = "42" });

            var method = typeof(MainViewModel).GetMethod("SaveProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            // Act
            await (Task)method!.Invoke(viewModel, null)!;

            // Assert
            Assert.True(fileSystem.Files.ContainsKey("project.mfp"));
            Assert.Contains("project saved", viewModel.StatusMessage);
            Assert.Contains("42", fileSystem.Files["project.mfp"]);
        }

        [Fact]
        public void CustomEntries_BoundCollection_ExposedFromCurrentConfig()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act
            viewModel.CustomEntries.Add(new CustomEntry { Address = 1, Type = "uint", Value = "1" });

            // Assert
            Assert.Single(viewModel.CustomEntries);
        }
    }
}

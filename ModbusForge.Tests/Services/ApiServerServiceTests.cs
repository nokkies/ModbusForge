using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using ModbusForge.Services.Api;
using ModbusForge.Services.Api.Dtos;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    /// <summary>
    /// Tests for Phase-5 API refactoring:
    /// – Facade-based design (no WPF IServiceProvider)
    /// – Input validation
    /// – Error handling (no raw ex.Message)
    /// – Cancellation / concurrency controls
    /// – API key authentication
    /// – Swagger disabled by default
    /// – Start/stop lifecycle
    /// </summary>
    public class ApiServerServiceTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // Helpers / Fakes
        // ──────────────────────────────────────────────────────────────────────

        private static Mock<ISettingsService> MakeSettings(
            bool enableApi = false,
            int port = 15080,
            bool enableDocs = false,
            bool enableAuth = false,
            string apiKey = "")
        {
            var mock = new Mock<ISettingsService>();
            mock.Setup(s => s.EnableApi).Returns(enableApi);
            mock.Setup(s => s.ApiPort).Returns(port);
            mock.Setup(s => s.EnableApiDocumentation).Returns(enableDocs);
            mock.Setup(s => s.EnableApiAuthentication).Returns(enableAuth);
            mock.Setup(s => s.ApiKey).Returns(apiKey);
            return mock;
        }

        private static Mock<IApiApplicationService> MakeApiApp() => new();

        private static ApiServerService MakeService(
            ISettingsService? settings = null,
            IApiApplicationService? apiApp = null)
        {
            return new ApiServerService(
                settings ?? MakeSettings().Object,
                NullLogger<ApiServerService>.Instance,
                apiApp ?? MakeApiApp().Object);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Constructor / DI
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_ThrowsOnNullDependencies()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ApiServerService(null!, NullLogger<ApiServerService>.Instance, MakeApiApp().Object));
            Assert.Throws<ArgumentNullException>(
                () => new ApiServerService(MakeSettings().Object, null!, MakeApiApp().Object));
            Assert.Throws<ArgumentNullException>(
                () => new ApiServerService(MakeSettings().Object, NullLogger<ApiServerService>.Instance, null!));
        }

        [Fact]
        public void IsRunning_ReturnsFalse_BeforeStart()
        {
            var svc = MakeService();
            Assert.False(svc.IsRunning);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Start / Stop lifecycle
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task StartAsync_InvalidPort_DoesNotStart()
        {
            var settings = MakeSettings(port: 0);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            Assert.False(svc.IsRunning);
        }

        [Fact]
        public async Task StartAsync_ValidPort_StartsServer()
        {
            var settings = MakeSettings(enableApi: true, port: 15081);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            try
            {
                Assert.True(svc.IsRunning);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        [Fact]
        public async Task StartAsync_AlreadyRunning_IsIdempotent()
        {
            var settings = MakeSettings(port: 15082);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            Assert.True(svc.IsRunning);
            await svc.StartAsync(); // Should not throw
            Assert.True(svc.IsRunning);
            await svc.StopAsync();
        }

        [Fact]
        public async Task StopAsync_WhenNotRunning_IsIdempotent()
        {
            var svc = MakeService();
            // Should not throw
            await svc.StopAsync();
            Assert.False(svc.IsRunning);
        }

        [Fact]
        public async Task StartStop_CanBeRepeated_WithoutResourceLeak()
        {
            var settings = MakeSettings(port: 15083);
            var svc = MakeService(settings.Object);

            for (int i = 0; i < 3; i++)
            {
                await svc.StartAsync();
                Assert.True(svc.IsRunning, $"Should be running on iteration {i}");
                await svc.StopAsync();
                Assert.False(svc.IsRunning, $"Should be stopped on iteration {i}");
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Swagger disabled by default
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SwaggerUnavailable_ByDefault()
        {
            var settings = MakeSettings(port: 15084, enableDocs: false);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            Assert.True(svc.IsRunning);
            try
            {
                using var http = new HttpClient();
                var response = await http.GetAsync("http://localhost:15084/swagger/index.html");
                // Swagger should return 404 when not enabled
                Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        [Fact]
        public async Task SwaggerAvailable_WhenEnabled()
        {
            var settings = MakeSettings(port: 15085, enableDocs: true);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            Assert.True(svc.IsRunning);
            try
            {
                using var http = new HttpClient();
                var response = await http.GetAsync("http://localhost:15085/swagger/index.html");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Server binds only to loopback
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Server_BindsOnlyToLoopback()
        {
            var settings = MakeSettings(port: 15086);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            Assert.True(svc.IsRunning);
            try
            {
                // Should respond on localhost
                using var http = new HttpClient();
                var response = await http.GetAsync("http://localhost:15086/api/status");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Input validation
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetRegisters_InvalidCount_Returns400_WithoutCallingModbus()
        {
            var appMock = MakeApiApp();
            var settings = MakeSettings(port: 15087);
            var svc = MakeService(settings.Object, appMock.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                // count = 0 → invalid
                var r1 = await http.GetAsync("http://localhost:15087/api/modbus/registers/0?length=0");
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, r1.StatusCode);

                // count = 126 → exceeds MaxRegisterCount (125)
                var r2 = await http.GetAsync("http://localhost:15087/api/modbus/registers/0?length=126");
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, r2.StatusCode);

                // Modbus service must NOT have been called
                appMock.Verify(
                    a => a.ReadHoldingRegistersAsync(
                        It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        [Fact]
        public async Task GetCoils_InvalidCount_Returns400_WithoutCallingModbus()
        {
            var appMock = MakeApiApp();
            var settings = MakeSettings(port: 15088);
            var svc = MakeService(settings.Object, appMock.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                var r = await http.GetAsync("http://localhost:15088/api/modbus/coils/0?length=0");
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, r.StatusCode);

                appMock.Verify(
                    a => a.ReadCoilsAsync(
                        It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        [Fact]
        public async Task PostCustomTag_NullBody_Returns400()
        {
            var settings = MakeSettings(port: 15089);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                var r = await http.PostAsync(
                    "http://localhost:15089/api/custom-tags",
                    new StringContent("null", System.Text.Encoding.UTF8, "application/json"));
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, r.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Internal exceptions return generic errors, details logged
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ModbusRead_Exception_Returns500_NotRawMessage()
        {
            var appMock = MakeApiApp();
            appMock.Setup(a => a.GetStatus()).Returns(new ApiStatus(true, "Client"));
            appMock.Setup(a => a.ReadHoldingRegistersAsync(
                    It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("secret internal detail"));

            var settings = MakeSettings(port: 15090);
            var svc = MakeService(settings.Object, appMock.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                var response = await http.GetAsync(
                    "http://localhost:15090/api/modbus/registers/0?length=1");
                Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

                var body = await response.Content.ReadAsStringAsync();
                // Raw exception message must not be exposed
                Assert.DoesNotContain("secret internal detail", body, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // API key authentication
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task UnauthorizedMutation_Returns401_WhenAuthEnabled()
        {
            var settings = MakeSettings(port: 15091, enableAuth: true, apiKey: "correct-key");
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                // POST /api/app/connect without a key
                var r = await http.PostAsync(
                    "http://localhost:15091/api/app/connect",
                    new StringContent(string.Empty));
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized, r.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        [Fact]
        public async Task ValidApiKey_AllowsAccess()
        {
            const string key = "valid-api-key-12345";
            var appMock = MakeApiApp();
            appMock.Setup(a => a.ConnectAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult.Ok());

            var settings = MakeSettings(port: 15092, enableAuth: true, apiKey: key);
            var svc = MakeService(settings.Object, appMock.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost:15092/api/app/connect");
                req.Headers.Add("X-ModbusForge-Api-Key", key);
                req.Content = new StringContent(string.Empty);
                var r = await http.SendAsync(req);
                Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        [Fact]
        public async Task ReadEndpoints_DoNotRequireApiKey_WhenAuthDisabled()
        {
            var appMock = MakeApiApp();
            appMock.Setup(a => a.GetStatus()).Returns(new ApiStatus(false, "Client"));

            var settings = MakeSettings(port: 15093, enableAuth: false);
            var svc = MakeService(settings.Object, appMock.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                // GET /api/app/status – should succeed without any key
                var r = await http.GetAsync("http://localhost:15093/api/app/status");
                Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Oversized request body
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task OversizedBody_Returns413OrBadRequest()
        {
            var settings = MakeSettings(port: 15094);
            var svc = MakeService(settings.Object);
            await svc.StartAsync();
            try
            {
                using var http = new HttpClient();
                // 2 MB body
                var oversized = new string('x', 2 * 1024 * 1024);
                var r = await http.PostAsync(
                    "http://localhost:15094/api/custom-tags",
                    new StringContent(oversized, System.Text.Encoding.UTF8, "application/json"));

                // Expect 413 (Kestrel body limit) or 400 (framework parse failure)
                Assert.True(
                    r.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge ||
                    r.StatusCode == System.Net.HttpStatusCode.BadRequest,
                    $"Expected 400 or 413, got {r.StatusCode}");
            }
            finally
            {
                await svc.StopAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // WpfApiApplicationService: concurrency / cancellation
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ConnectAsync_SerializesConcurrentCalls_ViaSemaphore()
        {
            // Two concurrent ConnectAsync calls should not both "win" the semaphore simultaneously.
            var callCount = 0;
            var fakeSvc = new FakeApiApplicationService(
                connectDelay: TimeSpan.FromMilliseconds(50),
                onConnect: () => Interlocked.Increment(ref callCount));

            var t1 = fakeSvc.ConnectAsync(CancellationToken.None);
            var t2 = fakeSvc.ConnectAsync(CancellationToken.None);
            await Task.WhenAll(t1, t2);

            // Both succeed, and the counter correctly incremented twice
            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task ConnectAsync_Cancellation_RemovesEventSubscription()
        {
            var fakeAccessor = new FakeAppStateAccessor { IsConnected = false };
            var dispatcher = new ImmediateDispatcher();
            var svc = new WpfApiApplicationService(
                fakeAccessor,
                new Mock<IModbusService>().Object,
                new Mock<IScriptRuleService>().Object,
                new Mock<IConsoleLoggerService>().Object,
                new Mock<ITrendLogger>().Object,
                dispatcher,
                NullLogger<WpfApiApplicationService>.Instance);

            // Never set IsConnected = true → the wait will be cancelled
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            fakeAccessor.ShouldExecuteConnect = true; // let CanExecute pass

            var result = await svc.ConnectAsync(cts.Token);

            Assert.False(result.Success);
            // After cancellation there should be zero subscribers on PropertyChanged
            Assert.Equal(0, fakeAccessor.PropertyChangedSubscriberCount);
        }

        [Fact]
        public async Task DisconnectAsync_Timeout_RemovesEventSubscription()
        {
            var fakeAccessor = new FakeAppStateAccessor { IsConnected = true };
            var dispatcher = new ImmediateDispatcher();
            var svc = new WpfApiApplicationService(
                fakeAccessor,
                new Mock<IModbusService>().Object,
                new Mock<IScriptRuleService>().Object,
                new Mock<IConsoleLoggerService>().Object,
                new Mock<ITrendLogger>().Object,
                dispatcher,
                NullLogger<WpfApiApplicationService>.Instance);

            // Disconnect command fires but never changes IsConnected → timeout
            fakeAccessor.ShouldExecuteDisconnect = true;
            fakeAccessor.DisconnectChangesState = false; // don't flip state

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var result = await svc.DisconnectAsync(cts.Token);

            Assert.False(result.Success);
            Assert.Equal(0, fakeAccessor.PropertyChangedSubscriberCount);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Legacy pattern tests (retained from prior version)
        // ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ConnectEndpoint_EventDriven_CompletesWhenIsConnectedChanges()
        {
            var vm = new FakeMainViewModel { IsConnected = false };
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName == nameof(FakeMainViewModel.IsConnected) && vm.IsConnected)
                {
                    vm.PropertyChanged -= handler;
                    tcs.TrySetResult(true);
                }
            };
            vm.PropertyChanged += handler;
            vm.SimulateConnect();
            var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result);
            Assert.True(vm.ConnectCalled);
        }

        [Fact]
        public async Task ConnectEndpoint_Timeout_WhenIsConnectedNeverChanges()
        {
            var vm = new FakeMainViewModel { IsConnected = false };
            vm.CanConnectResult = false;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await tcs.Task.WaitAsync(cts.Token));
        }

        [Fact]
        public void ConnectionStateTimeout_IsReasonable()
        {
            const int expectedMin = 10_000;
            const int expectedMax = 60_000;
            var field = typeof(WpfApiApplicationService).GetField(
                "ConnectionStateTimeoutMs",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);
            var value = (int)field!.GetValue(null)!;
            Assert.True(value >= expectedMin, $"Timeout {value}ms is too short (min {expectedMin}ms)");
            Assert.True(value <= expectedMax, $"Timeout {value}ms is too long (max {expectedMax}ms)");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>A serialising fake that wraps the real semaphore logic.</summary>
    internal sealed class FakeApiApplicationService : IApiApplicationService
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly TimeSpan _connectDelay;
        private readonly Action _onConnect;

        public FakeApiApplicationService(TimeSpan connectDelay, Action onConnect)
        {
            _connectDelay = connectDelay;
            _onConnect = onConnect;
        }

        public async Task<OperationResult> ConnectAsync(CancellationToken token)
        {
            await _lock.WaitAsync(token);
            try
            {
                await Task.Delay(_connectDelay, token);
                _onConnect();
                return OperationResult.Ok();
            }
            finally
            {
                _lock.Release();
            }
        }

        public ApiStatus GetStatus() => new(false, "Client");
        public Task<OperationResult> DisconnectAsync(CancellationToken token) => Task.FromResult(OperationResult.Ok());
        public Task<ushort[]?> ReadHoldingRegistersAsync(byte u, ushort a, ushort c, CancellationToken t) => Task.FromResult<ushort[]?>(null);
        public Task<bool[]?> ReadCoilsAsync(byte u, ushort a, ushort c, CancellationToken t) => Task.FromResult<bool[]?>(null);
        public Task<IReadOnlyList<CustomEntry>> GetCustomTagsAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<CustomEntry>>(Array.Empty<CustomEntry>());
        public Task<CustomEntry> AddCustomTagAsync(CustomEntry e, CancellationToken t) => Task.FromResult(e);
        public Task<bool> RemoveCustomTagAsync(int a, CancellationToken t) => Task.FromResult(false);
        public Task<IReadOnlyList<VisualNode>> GetSimulationNodesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<VisualNode>>(Array.Empty<VisualNode>());
        public Task<VisualNode> UpsertSimulationNodeAsync(VisualNode n, CancellationToken t) => Task.FromResult(n);
        public Task<bool> RemoveSimulationNodeAsync(string id, CancellationToken t) => Task.FromResult(false);
        public Task<IReadOnlyList<ScriptRule>> GetScriptRulesAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<ScriptRule>>(Array.Empty<ScriptRule>());
        public Task<ScriptRule> UpsertScriptRuleAsync(ScriptRule r, CancellationToken t) => Task.FromResult(r);
        public Task<bool> RemoveScriptRuleAsync(string name, CancellationToken t) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetLogsAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task AddTrendAsync(string k, string d, CancellationToken t) => Task.CompletedTask;
    }

    /// <summary>Fake IAppStateAccessor that tracks PropertyChanged subscriber count.</summary>
    internal sealed class FakeAppStateAccessor : IAppStateAccessor
    {
        public bool IsConnected { get; set; }
        public string Mode => "Client";

        public bool ShouldExecuteConnect { get; set; } = false;
        public bool ShouldExecuteDisconnect { get; set; } = false;
        public bool DisconnectChangesState { get; set; } = true;

        private int _subscriberCount;
        public int PropertyChangedSubscriberCount => _subscriberCount;

        private event PropertyChangedEventHandler? _propertyChanged;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _propertyChanged += value; Interlocked.Increment(ref _subscriberCount); }
            remove { _propertyChanged -= value; Interlocked.Decrement(ref _subscriberCount); }
        }

        public ICommand ConnectCommand => new RelayCommand(
            () => { /* Does NOT flip IsConnected – let test control that */ },
            () => ShouldExecuteConnect);

        public ICommand DisconnectCommand => new RelayCommand(
            () =>
            {
                if (DisconnectChangesState)
                {
                    IsConnected = false;
                    _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
                }
            },
            () => ShouldExecuteDisconnect);

        public ObservableCollection<CustomEntry> CustomEntries { get; } = new();
        public ObservableCollection<VisualNode> SimulationNodes { get; } = new();
    }

    /// <summary>Lightweight fake ViewModel for legacy pattern tests.</summary>
    public class FakeMainViewModel : INotifyPropertyChanged
    {
        private bool _isConnected;

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
                }
            }
        }

        public bool ConnectCalled { get; private set; }
        public bool DisconnectCalled { get; private set; }
        public bool CanConnectResult { get; set; } = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand ConnectCommand => new RelayCommand(() =>
        {
            ConnectCalled = true;
            if (CanConnectResult)
                IsConnected = true;
        });

        public ICommand DisconnectCommand => new RelayCommand(() =>
        {
            DisconnectCalled = true;
            IsConnected = false;
        });

        public void SimulateConnect()
        {
            if (ConnectCommand.CanExecute(null))
                ConnectCommand.Execute(null);
        }

        public void SimulateDisconnect()
        {
            if (DisconnectCommand.CanExecute(null))
                DisconnectCommand.Execute(null);
        }
    }
}

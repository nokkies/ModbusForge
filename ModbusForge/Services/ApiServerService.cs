using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.ViewModels;
using System.Windows;
using ModbusForge.Models;

namespace ModbusForge.Services;

public class ApiServerService : IApiServerService
{
    private const int ConnectionStateTimeoutMs = 30000;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<ApiServerService> _logger;
    private readonly IServiceProvider _wpfServiceProvider;

    private WebApplication? _app;
    private MainViewModel? _mainViewModel;

    public bool IsRunning => _app != null;

    public ApiServerService(
        ISettingsService settingsService,
        ILogger<ApiServerService> logger,
        IServiceProvider wpfServiceProvider)
    {
        _settingsService = settingsService;
        _logger = logger;
        _wpfServiceProvider = wpfServiceProvider;
    }

    private MainViewModel MainViewModel
    {
        get
        {
            _mainViewModel ??= _wpfServiceProvider.GetRequiredService<MainViewModel>();
            return _mainViewModel;
        }
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        try
        {
            var builder = WebApplication.CreateBuilder();

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Add endpoints api explorer and swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Inject WPF services into the ASP.NET Core DI container
            builder.Services.AddSingleton(_settingsService);
            builder.Services.AddSingleton(MainViewModel);
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IConnectionManager>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IVisualSimulationService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IScriptRuleService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<ICustomEntryService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IConsoleLoggerService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<ITrendLogger>());
            builder.Services.AddSingleton<IModbusService>(sp =>
            {
                // Resolve the active service at call time based on VM mode
                var vm = sp.GetRequiredService<MainViewModel>();
                if (vm.IsServerMode)
                    return _wpfServiceProvider.GetRequiredService<ModbusServerService>();
                return _wpfServiceProvider.GetRequiredService<ModbusTcpService>();
            });

            var port = _settingsService.ApiPort;
            builder.WebHost.UseUrls($"http://localhost:{port}");

            _app = builder.Build();

            // Configure the HTTP request pipeline.
            _app.UseSwagger();
            _app.UseSwaggerUI();

            // Map Endpoints
            MapEndpoints(_app);

            await _app.StartAsync();
            _logger.LogInformation($"API Server started on port {port}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start API Server.");
            _app = null;
        }
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            try
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping API Server.");
            }
            finally
            {
                _app = null;
                _logger.LogInformation("API Server stopped.");
            }
        }
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/status", () => Results.Ok(new { Status = "Running" }))
           .WithTags("System");

        // --- Application State Endpoints ---
        app.MapGet("/api/app/status", (MainViewModel vm) => Results.Ok(new { IsConnected = vm.IsConnected }))
           .WithTags("Application");

        app.MapPost("/api/app/connect", async (MainViewModel vm, CancellationToken ct) =>
        {
            bool initiated = false;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!vm.IsConnected && vm.ConnectCommand.CanExecute(null))
                {
                    vm.ConnectCommand.Execute(null);
                    initiated = true;
                }
            });

            if (!initiated)
                return Results.BadRequest(new { Error = "Already connected or cannot connect." });

            if (vm.IsConnected)
                return Results.Ok(new { Success = true });

            // Event-driven wait instead of polling
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsConnected) && vm.IsConnected)
                {
                    vm.PropertyChanged -= handler;
                    tcs.TrySetResult(true);
                }
            };
            vm.PropertyChanged += handler;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectionStateTimeoutMs);
            try
            {
                await tcs.Task.WaitAsync(timeoutCts.Token);
                return Results.Ok(new { Success = true });
            }
            catch (OperationCanceledException)
            {
                vm.PropertyChanged -= handler;
                return Results.BadRequest(new { Error = "Connection attempt timed out." });
            }
        }).WithTags("Application");

        app.MapPost("/api/app/disconnect", async (MainViewModel vm, CancellationToken ct) =>
        {
            bool initiated = false;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (vm.IsConnected && vm.DisconnectCommand.CanExecute(null))
                {
                    vm.DisconnectCommand.Execute(null);
                    initiated = true;
                }
            });

            if (!initiated)
                return Results.BadRequest(new { Error = "Already disconnected or cannot disconnect." });

            if (!vm.IsConnected)
                return Results.Ok(new { Success = true });

            // Event-driven wait instead of polling
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsConnected) && !vm.IsConnected)
                {
                    vm.PropertyChanged -= handler;
                    tcs.TrySetResult(true);
                }
            };
            vm.PropertyChanged += handler;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectionStateTimeoutMs);
            try
            {
                await tcs.Task.WaitAsync(timeoutCts.Token);
                return Results.Ok(new { Success = true });
            }
            catch (OperationCanceledException)
            {
                vm.PropertyChanged -= handler;
                return Results.BadRequest(new { Error = "Disconnect timed out." });
            }
        }).WithTags("Application");

        // --- Modbus Operations ---
        app.MapGet("/api/modbus/registers/{address}", async (MainViewModel vm, IModbusService modbusService, [FromRoute] ushort address, [FromQuery] int length = 1) =>
        {
            if (!vm.IsConnected)
                return Results.BadRequest(new { Error = "Not connected." });

            try
            {
                var values = await modbusService.ReadHoldingRegistersAsync(vm.EffectiveUnitId, address, length);
                if (values != null)
                    return Results.Ok(new { Address = address, Length = length, Data = values });

                return Results.BadRequest(new { Error = "Failed to read registers from device." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        }).WithTags("Modbus");

        app.MapGet("/api/modbus/coils/{address}", async (MainViewModel vm, IModbusService modbusService, [FromRoute] ushort address, [FromQuery] int length = 1) =>
        {
            if (!vm.IsConnected)
                return Results.BadRequest(new { Error = "Not connected." });

            try
            {
                var values = await modbusService.ReadCoilsAsync(vm.EffectiveUnitId, address, length);
                if (values != null)
                    return Results.Ok(new { Address = address, Length = length, Data = values });

                return Results.BadRequest(new { Error = "Failed to read coils from device." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        }).WithTags("Modbus");

        // --- Custom Tags ---
        app.MapGet("/api/custom-tags", async (MainViewModel vm) =>
        {
            var snapshot = await Application.Current.Dispatcher.InvokeAsync(() => vm.CustomEntries.ToList());
            return Results.Ok(snapshot);
        }).WithTags("CustomTags");

        app.MapPost("/api/custom-tags", async (MainViewModel vm, [FromBody] CustomEntry entry) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(() => vm.CustomEntries.Add(entry));
            return Results.Ok(entry);
        }).WithTags("CustomTags");

        app.MapDelete("/api/custom-tags/{address}", async (MainViewModel vm, [FromRoute] int address) =>
        {
            var removed = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var entry = vm.CustomEntries.FirstOrDefault(e => e.Address == address);
                if (entry != null)
                {
                    vm.CustomEntries.Remove(entry);
                    return true;
                }
                return false;
            });
            return removed ? Results.Ok() : Results.NotFound();
        }).WithTags("CustomTags");

        // --- Simulation Nodes ---
        app.MapGet("/api/simulation/nodes", async (MainViewModel vm) =>
        {
            var snapshot = await Application.Current.Dispatcher.InvokeAsync(() => vm.CurrentConfig.SimulationSettings.VisualNodes.ToList());
            return Results.Ok(snapshot);
        }).WithTags("Simulation");

        app.MapPost("/api/simulation/nodes", async (MainViewModel vm, [FromBody] VisualNode node) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = vm.CurrentConfig.SimulationSettings.VisualNodes.FirstOrDefault(n => n.Id == node.Id);
                if (existing != null)
                    vm.CurrentConfig.SimulationSettings.VisualNodes.Remove(existing);
                vm.CurrentConfig.SimulationSettings.VisualNodes.Add(node);
            });
            return Results.Ok(node);
        }).WithTags("Simulation");

        app.MapDelete("/api/simulation/nodes/{id}", async (MainViewModel vm, [FromRoute] string id) =>
        {
            var removed = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = vm.CurrentConfig.SimulationSettings.VisualNodes.FirstOrDefault(n => n.Id == id);
                if (existing != null)
                {
                    vm.CurrentConfig.SimulationSettings.VisualNodes.Remove(existing);
                    return true;
                }
                return false;
            });
            return removed ? Results.Ok() : Results.NotFound();
        }).WithTags("Simulation");

        // --- Scripts ---
        app.MapGet("/api/scripts", async (IScriptRuleService scriptService) =>
        {
            var snapshot = await Application.Current.Dispatcher.InvokeAsync(() => scriptService.Rules.ToList());
            return Results.Ok(snapshot);
        }).WithTags("Scripts");

        app.MapPost("/api/scripts", async (IScriptRuleService scriptService, [FromBody] ScriptRule rule) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = scriptService.Rules.FirstOrDefault(r => r.Name == rule.Name);
                if (existing != null)
                    scriptService.RemoveRule(existing);
                scriptService.AddRule(rule);
            });
            return Results.Ok(rule);
        }).WithTags("Scripts");

        app.MapDelete("/api/scripts/{name}", async (IScriptRuleService scriptService, [FromRoute] string name) =>
        {
            var removed = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existing = scriptService.Rules.FirstOrDefault(r => r.Name == name);
                if (existing != null)
                {
                    scriptService.RemoveRule(existing);
                    return true;
                }
                return false;
            });
            return removed ? Results.Ok() : Results.NotFound();
        }).WithTags("Scripts");

        // --- Logs ---
        app.MapGet("/api/logs", async (IConsoleLoggerService loggerService) =>
        {
            var snapshot = await Application.Current.Dispatcher.InvokeAsync(() => loggerService.LogMessages.ToList());
            return Results.Ok(snapshot);
        }).WithTags("Logs");

        // --- Trends ---
        app.MapPost("/api/trends/{key}", async (ITrendLogger trendLogger, [FromRoute] string key, [FromQuery] string displayName) =>
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                trendLogger.Add(key, string.IsNullOrEmpty(displayName) ? key : displayName));
            return Results.Ok(new { Success = true });
        }).WithTags("Trends");
    }
}

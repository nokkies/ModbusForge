using System;
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

public class ApiServerService : IApiServerService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ApiServerService> _logger;
    private readonly IServiceProvider _wpfServiceProvider;

    private WebApplication? _app;

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
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<MainViewModel>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IConnectionManager>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IVisualSimulationService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IScriptRuleService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<ICustomEntryService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<IConsoleLoggerService>());
            builder.Services.AddSingleton(_wpfServiceProvider.GetRequiredService<ITrendLogger>());

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

        app.MapPost("/api/app/connect", async (MainViewModel vm) => {
            if (!vm.IsConnected && vm.ConnectCommand.CanExecute(null))
            {
                vm.ConnectCommand.Execute(null);
                await Task.Delay(500);
                return Results.Ok(new { Success = vm.IsConnected });
            }
            return Results.BadRequest(new { Error = "Already connected or cannot connect." });
        }).WithTags("Application");

        app.MapPost("/api/app/disconnect", async (MainViewModel vm) => {
            if (vm.IsConnected && vm.DisconnectCommand.CanExecute(null))
            {
                vm.DisconnectCommand.Execute(null);
                await Task.Delay(500);
                return Results.Ok(new { Success = !vm.IsConnected });
            }
            return Results.BadRequest(new { Error = "Already disconnected or cannot disconnect." });
        }).WithTags("Application");

        // --- Modbus Operations ---
        app.MapGet("/api/modbus/registers/{address}", async (MainViewModel vm, [FromRoute] ushort address, [FromQuery] int length = 1) => {
            vm.RegisterStart = address;
            vm.RegisterCount = length;
            if (vm.ReadRegistersCommand.CanExecute(null))
            {
                 vm.ReadRegistersCommand.Execute(null);
                 await Task.Delay(200);
                 return Results.Ok(new { Address = address, Length = length, Data = vm.HoldingRegisters });
            }
            return Results.BadRequest(new { Error = "Cannot read registers." });
        }).WithTags("Modbus");

        app.MapGet("/api/modbus/coils/{address}", async (MainViewModel vm, [FromRoute] ushort address, [FromQuery] int length = 1) => {
            vm.CoilStart = address;
            vm.CoilCount = length;
            if (vm.ReadCoilsCommand.CanExecute(null))
            {
                 vm.ReadCoilsCommand.Execute(null);
                 await Task.Delay(200);
                 return Results.Ok(new { Address = address, Length = length, Data = vm.Coils });
            }
            return Results.BadRequest(new { Error = "Cannot read coils." });
        }).WithTags("Modbus");

        // --- Custom Tags ---
        app.MapGet("/api/custom-tags", (MainViewModel vm) => {
            return Results.Ok(vm.CustomEntries);
        }).WithTags("CustomTags");

        app.MapPost("/api/custom-tags", (MainViewModel vm, [FromBody] CustomEntry entry) => {
            vm.CustomEntries.Add(entry);
            return Results.Ok(entry);
        }).WithTags("CustomTags");

        app.MapDelete("/api/custom-tags/{address}", (MainViewModel vm, [FromRoute] int address) => {
            var entry = vm.CustomEntries.FirstOrDefault(e => e.Address == address);
            if (entry != null)
            {
                vm.CustomEntries.Remove(entry);
                return Results.Ok();
            }
            return Results.NotFound();
        }).WithTags("CustomTags");

        // --- Simulation Nodes ---
        app.MapGet("/api/simulation/nodes", (MainViewModel vm) => {
            return Results.Ok(vm.CurrentConfig.SimulationSettings.VisualNodes);
        }).WithTags("Simulation");

        app.MapPost("/api/simulation/nodes", (MainViewModel vm, [FromBody] VisualNode node) => {
            var existing = vm.CurrentConfig.SimulationSettings.VisualNodes.FirstOrDefault(n => n.Id == node.Id);
            if (existing != null)
            {
                vm.CurrentConfig.SimulationSettings.VisualNodes.Remove(existing);
            }
            vm.CurrentConfig.SimulationSettings.VisualNodes.Add(node);
            return Results.Ok(node);
        }).WithTags("Simulation");

        app.MapDelete("/api/simulation/nodes/{id}", (MainViewModel vm, [FromRoute] string id) => {
            var existing = vm.CurrentConfig.SimulationSettings.VisualNodes.FirstOrDefault(n => n.Id == id);
            if (existing != null)
            {
                vm.CurrentConfig.SimulationSettings.VisualNodes.Remove(existing);
                return Results.Ok();
            }
            return Results.NotFound();
        }).WithTags("Simulation");

        // --- Scripts ---
        app.MapGet("/api/scripts", (IScriptRuleService scriptService) => {
            return Results.Ok(scriptService.Rules);
        }).WithTags("Scripts");

        app.MapPost("/api/scripts", async (IScriptRuleService scriptService, [FromBody] ScriptRule rule) => {
            await Application.Current.Dispatcher.InvokeAsync(() => {
                var existing = scriptService.Rules.FirstOrDefault(r => r.Name == rule.Name);
                if (existing != null)
                {
                    scriptService.RemoveRule(existing);
                }
                scriptService.AddRule(rule);
            });
            return Results.Ok(rule);
        }).WithTags("Scripts");

        app.MapDelete("/api/scripts/{name}", async (IScriptRuleService scriptService, [FromRoute] string name) => {
            var existing = scriptService.Rules.FirstOrDefault(r => r.Name == name);
            if (existing != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => {
                    scriptService.RemoveRule(existing);
                });
                return Results.Ok();
            }
            return Results.NotFound();
        }).WithTags("Scripts");

        // --- Logs ---
        app.MapGet("/api/logs", (IConsoleLoggerService loggerService) => {
            return Results.Ok(loggerService.LogMessages);
        }).WithTags("Logs");

        // --- Trends ---
        app.MapPost("/api/trends/{key}", async (ITrendLogger trendLogger, [FromRoute] string key, [FromQuery] string displayName) => {
            await Application.Current.Dispatcher.InvokeAsync(() => {
                trendLogger.Add(key, string.IsNullOrEmpty(displayName) ? key : displayName);
            });
            return Results.Ok(new { Success = true });
        }).WithTags("Trends");
    }

    public void Dispose()
    {
        if (_app != null)
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}

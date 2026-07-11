using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.Services.Api;
using ModbusForge.Services.Api.Dtos;
using ModbusForge.Models;

namespace ModbusForge.Services;

public class ApiServerService : IApiServerService
{
    // ─── Constants ────────────────────────────────────────────────────────────
    private const int ConnectionStateTimeoutMs = 30_000;
    private const int MaxRequestBodyBytes = 1 * 1024 * 1024; // 1 MB

    // Modbus protocol limits
    private const int MaxRegisterCount = 125;
    private const int MaxCoilCount = 2000;
    private const int MaxAddress = 65535;

    // String-field limits (DTOs carry annotations; endpoints re-validate these)
    private const int MaxNameLength = 128;
    private const int MaxKeyLength = 128;

    // Rate-limiting policy name
    private const string RateLimitPolicy = "ApiPolicy";

    // API key header name (never logged)
    private const string ApiKeyHeader = "X-ModbusForge-Api-Key";

    // ─── Fields ───────────────────────────────────────────────────────────────
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ApiServerService> _logger;
    private readonly IApiApplicationService _apiApp;

    private WebApplication? _app;

    public bool IsRunning => _app != null;

    // ─── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Primary constructor. The WPF <see cref="IServiceProvider"/> is NO LONGER injected;
    /// all application interactions go through the focused <see cref="IApiApplicationService"/> facade.
    /// </summary>
    public ApiServerService(
        ISettingsService settingsService,
        ILogger<ApiServerService> logger,
        IApiApplicationService apiApp)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiApp = apiApp ?? throw new ArgumentNullException(nameof(apiApp));
    }

    // ─── Start / Stop ─────────────────────────────────────────────────────────

    public async Task StartAsync()
    {
        if (IsRunning) return;

        // Validate port before attempting to bind.
        var port = _settingsService.ApiPort;
        if (port < 1 || port > 65535)
        {
            _logger.LogError("Invalid API port {Port}; must be 1-65535. API server not started.", port);
            return;
        }

        try
        {
            var builder = WebApplication.CreateBuilder();

            // ── Logging ───────────────────────────────────────────────────────
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // ── Swagger (opt-in only) ─────────────────────────────────────────
            if (_settingsService.EnableApiDocumentation)
            {
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen(options =>
                {
                    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                    {
                        Title = "ModbusForge API",
                        Version = "v1"
                    });
                    // Document the API key header
                    options.AddSecurityDefinition(ApiKeyHeader, new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                        Name = ApiKeyHeader,
                        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
                    });
                });
            }

            // ── Rate limiting (built-in ASP.NET Core 7+) ─────────────────────
            builder.Services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter(RateLimitPolicy, opt =>
                {
                    opt.PermitLimit = 60;
                    opt.Window = TimeSpan.FromSeconds(60);
                    opt.QueueLimit = 0;
                });
                options.OnRejected = async (ctx, _) =>
                {
                    ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await ctx.HttpContext.Response.WriteAsJsonAsync(
                        ApiError.TooManyRequests());
                };
            });

            // ── Register the facade so endpoints can resolve it ───────────────
            builder.Services.AddSingleton(_apiApp);

            // ── Bind only to loopback ─────────────────────────────────────────
            builder.WebHost.UseUrls($"http://localhost:{port}");

            // ── Request body size limit ───────────────────────────────────────
            builder.WebHost.ConfigureKestrel(kestrel =>
                kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

            _app = builder.Build();

            // ── Middleware pipeline ───────────────────────────────────────────
            _app.UseRateLimiter();

            if (_settingsService.EnableApiDocumentation)
            {
                _app.UseSwagger();
                _app.UseSwaggerUI();
            }

            MapEndpoints(_app);

            await _app.StartAsync();
            _logger.LogInformation("API Server started on http://localhost:{Port}", port);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
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
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
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

    // ─── Endpoint mapping ─────────────────────────────────────────────────────

    private void MapEndpoints(WebApplication app)
    {
        var apiGroup = app.MapGroup("/api").RequireRateLimiting(RateLimitPolicy);

        // ── System ────────────────────────────────────────────────────────────
        apiGroup.MapGet("/status", () => Results.Ok(new { Status = "Running" }))
            .WithTags("System");

        // ── Application state ─────────────────────────────────────────────────
        var appGroup = apiGroup.MapGroup("/app").WithTags("Application");

        appGroup.MapGet("/status", (IApiApplicationService svc) =>
            Results.Ok(svc.GetStatus()));

        appGroup.MapPost("/connect", async (
            IApiApplicationService svc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            try
            {
                var result = await svc.ConnectAsync(ct);
                return result.Success
                    ? Results.Ok(new ApiOperationResult(true))
                    : Results.BadRequest(ApiError.BadRequest(result.Error ?? "Cannot connect."));
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499); // Client Closed Request
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException))
            {
                _logger.LogError(ex, "Unhandled exception in POST /api/app/connect.");
                return Results.Problem(
                    title: ApiError.InternalError().Title,
                    statusCode: 500);
            }
        });

        appGroup.MapPost("/disconnect", async (
            IApiApplicationService svc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            try
            {
                var result = await svc.DisconnectAsync(ct);
                return result.Success
                    ? Results.Ok(new ApiOperationResult(true))
                    : Results.BadRequest(ApiError.BadRequest(result.Error ?? "Cannot disconnect."));
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException))
            {
                _logger.LogError(ex, "Unhandled exception in POST /api/app/disconnect.");
                return Results.Problem(
                    title: ApiError.InternalError().Title,
                    statusCode: 500);
            }
        });

        // ── Modbus ────────────────────────────────────────────────────────────
        var modbusGroup = apiGroup.MapGroup("/modbus").WithTags("Modbus");

        modbusGroup.MapGet("/registers/{address}", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromRoute] ushort address,
            [FromQuery] int length,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;

            // Validation
            if (length < 1 || length > MaxRegisterCount)
                return Results.BadRequest(ApiError.BadRequest(
                    $"length must be 1..{MaxRegisterCount}."));
            if (address + length - 1 > MaxAddress)
                return Results.BadRequest(ApiError.BadRequest(
                    "address + length exceeds maximum address 65535."));

            var status = svc.GetStatus();
            if (!status.IsConnected)
                return Results.BadRequest(ApiError.BadRequest("Not connected."));

            try
            {
                var unitId = GetUnitIdFromQuery(ctx);
                var values = await svc.ReadHoldingRegistersAsync(
                    unitId, address, (ushort)length, ct);
                if (values is null)
                    return Results.BadRequest(ApiError.BadRequest(
                        "Failed to read registers from device."));
                return Results.Ok(new { Address = address, Length = length, Data = values });
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException))
            {
                _logger.LogError(ex, "Unhandled exception reading holding registers at {Address}.", address);
                return Results.Problem(
                    title: ApiError.InternalError().Title,
                    statusCode: 500);
            }
        });

        modbusGroup.MapGet("/coils/{address}", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromRoute] ushort address,
            [FromQuery] int length,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;

            if (length < 1 || length > MaxCoilCount)
                return Results.BadRequest(ApiError.BadRequest(
                    $"length must be 1..{MaxCoilCount}."));
            if (address + length - 1 > MaxAddress)
                return Results.BadRequest(ApiError.BadRequest(
                    "address + length exceeds maximum address 65535."));

            var status = svc.GetStatus();
            if (!status.IsConnected)
                return Results.BadRequest(ApiError.BadRequest("Not connected."));

            try
            {
                var unitId = GetUnitIdFromQuery(ctx);
                var values = await svc.ReadCoilsAsync(
                    unitId, address, (ushort)length, ct);
                if (values is null)
                    return Results.BadRequest(ApiError.BadRequest(
                        "Failed to read coils from device."));
                return Results.Ok(new { Address = address, Length = length, Data = values });
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException))
            {
                _logger.LogError(ex, "Unhandled exception reading coils at {Address}.", address);
                return Results.Problem(
                    title: ApiError.InternalError().Title,
                    statusCode: 500);
            }
        });

        // ── Custom tags ───────────────────────────────────────────────────────
        var tagsGroup = apiGroup.MapGroup("/custom-tags").WithTags("CustomTags");

        tagsGroup.MapGet("/", async (IApiApplicationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCustomTagsAsync(ct)));

        tagsGroup.MapPost("/", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromBody] CreateCustomEntryRequest? req,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (req is null)
                return Results.BadRequest(ApiError.BadRequest("Request body is required."));

            var validation = ValidateCreateCustomEntryRequest(req);
            if (validation is not null) return validation;

            var entry = MapToCustomEntry(req);
            var created = await svc.AddCustomTagAsync(entry, ct);
            return Results.Ok(created);
        });

        tagsGroup.MapDelete("/{address}", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromRoute] int address,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (address < 0 || address > MaxAddress)
                return Results.BadRequest(ApiError.BadRequest("address must be 0..65535."));

            var removed = await svc.RemoveCustomTagAsync(address, ct);
            return removed ? Results.Ok() : Results.NotFound();
        });

        // ── Simulation nodes ──────────────────────────────────────────────────
        var simGroup = apiGroup.MapGroup("/simulation/nodes").WithTags("Simulation");

        simGroup.MapGet("/", async (IApiApplicationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetSimulationNodesAsync(ct)));

        simGroup.MapPost("/", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromBody] CreateSimulationNodeRequest? req,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (req is null)
                return Results.BadRequest(ApiError.BadRequest("Request body is required."));

            var validation = ValidateCreateSimulationNodeRequest(req);
            if (validation is not null) return validation;

            var node = MapToVisualNode(req);
            var upserted = await svc.UpsertSimulationNodeAsync(node, ct);
            return Results.Ok(upserted);
        });

        simGroup.MapDelete("/{id}", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromRoute] string id,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (string.IsNullOrWhiteSpace(id) || id.Length > MaxKeyLength)
                return Results.BadRequest(ApiError.BadRequest("id must be non-empty and ≤128 characters."));

            var removed = await svc.RemoveSimulationNodeAsync(id, ct);
            return removed ? Results.Ok() : Results.NotFound();
        });

        // ── Scripts ───────────────────────────────────────────────────────────
        var scriptsGroup = apiGroup.MapGroup("/scripts").WithTags("Scripts");

        scriptsGroup.MapGet("/", async (IApiApplicationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetScriptRulesAsync(ct)));

        scriptsGroup.MapPost("/", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromBody] CreateScriptRuleRequest? req,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (req is null)
                return Results.BadRequest(ApiError.BadRequest("Request body is required."));

            var validation = ValidateCreateScriptRuleRequest(req);
            if (validation is not null) return validation;

            var rule = MapToScriptRule(req);
            var upserted = await svc.UpsertScriptRuleAsync(rule, ct);
            return Results.Ok(upserted);
        });

        scriptsGroup.MapDelete("/{name}", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromRoute] string name,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
                return Results.BadRequest(ApiError.BadRequest("name must be non-empty and ≤128 characters."));

            var removed = await svc.RemoveScriptRuleAsync(name, ct);
            return removed ? Results.Ok() : Results.NotFound();
        });

        // ── Logs ─────────────────────────────────────────────────────────────
        apiGroup.MapGet("/logs", async (IApiApplicationService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetLogsAsync(ct)))
            .WithTags("Logs");

        // ── Trends ───────────────────────────────────────────────────────────
        apiGroup.MapPost("/trends/{key}", async (
            IApiApplicationService svc,
            HttpContext ctx,
            [FromRoute] string key,
            [FromQuery] string? displayName,
            CancellationToken ct) =>
        {
            if (!RequireApiKey(ctx, out var authResult))
                return authResult!;
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
                return Results.BadRequest(ApiError.BadRequest("key must be non-empty and ≤128 characters."));
            if (displayName is not null && displayName.Length > MaxKeyLength)
                return Results.BadRequest(ApiError.BadRequest("displayName must be ≤128 characters."));

            await svc.AddTrendAsync(key, displayName ?? key, ct);
            return Results.Ok(new ApiOperationResult(true));
        }).WithTags("Trends");
    }

    // ─── API key authentication ───────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when:
    /// (a) API key auth is disabled via settings, OR
    /// (b) the request supplies a key that matches (compared in constant time).
    /// The key value is NEVER logged.
    /// </summary>
    private bool RequireApiKey(HttpContext ctx, out IResult? failureResult)
    {
        if (!_settingsService.EnableApiAuthentication)
        {
            failureResult = null;
            return true;
        }

        var expectedKey = _settingsService.ApiKey;
        if (string.IsNullOrEmpty(expectedKey))
        {
            // Key not configured yet – log a warning (but not the key) and deny.
            _logger.LogWarning("API key authentication is enabled but no key is configured. Denying request.");
            failureResult = Results.Json(ApiError.Unauthorized(), statusCode: StatusCodes.Status401Unauthorized);
            return false;
        }

        ctx.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey);
        var providedKeyStr = providedKey.ToString();

        // Constant-time comparison to resist timing attacks.
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(providedKeyStr),
                System.Text.Encoding.UTF8.GetBytes(expectedKey)))
        {
            _logger.LogWarning("Unauthorized API request from {RemoteIp}.", ctx.Connection.RemoteIpAddress);
            failureResult = Results.Json(ApiError.Unauthorized(), statusCode: StatusCodes.Status401Unauthorized);
            return false;
        }

        failureResult = null;
        return true;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static byte GetUnitIdFromQuery(HttpContext ctx)
    {
        if (ctx.Request.Query.TryGetValue("unitId", out var raw)
            && byte.TryParse(raw, out var id))
            return id;
        return 1;
    }

    // ─── Input validation helpers ─────────────────────────────────────────────

    private static IResult? ValidateCreateCustomEntryRequest(CreateCustomEntryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > MaxNameLength)
            return Results.BadRequest(ApiError.BadRequest("Name must be non-empty and ≤128 characters."));
        if (req.Address < 0 || req.Address > MaxAddress)
            return Results.BadRequest(ApiError.BadRequest("Address must be 0..65535."));
        if (req.Value.Length > 64)
            return Results.BadRequest(ApiError.BadRequest("Value must be ≤64 characters."));
        if (req.PeriodMs < 100)
            return Results.BadRequest(ApiError.BadRequest("PeriodMs must be ≥100."));
        if (req.ReadPeriodMs < 100)
            return Results.BadRequest(ApiError.BadRequest("ReadPeriodMs must be ≥100."));
        return null;
    }

    private static IResult? ValidateCreateSimulationNodeRequest(CreateSimulationNodeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > MaxNameLength)
            return Results.BadRequest(ApiError.BadRequest("Name must be non-empty and ≤128 characters."));
        if (req.PeriodMs < 10)
            return Results.BadRequest(ApiError.BadRequest("PeriodMs must be ≥10."));
        if (req.Width < 50 || req.Height < 50)
            return Results.BadRequest(ApiError.BadRequest("Width and Height must be ≥50."));
        return null;
    }

    private static IResult? ValidateCreateScriptRuleRequest(CreateScriptRuleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > MaxNameLength)
            return Results.BadRequest(ApiError.BadRequest("Name must be non-empty and ≤128 characters."));
        if (req.TriggerAddress < 0 || req.TriggerAddress > MaxAddress)
            return Results.BadRequest(ApiError.BadRequest("TriggerAddress must be 0..65535."));
        if (req.ActionAddress < 0 || req.ActionAddress > MaxAddress)
            return Results.BadRequest(ApiError.BadRequest("ActionAddress must be 0..65535."));
        if (req.TriggerValue.Length > 64)
            return Results.BadRequest(ApiError.BadRequest("TriggerValue must be ≤64 characters."));
        if (req.ActionValue.Length > 64)
            return Results.BadRequest(ApiError.BadRequest("ActionValue must be ≤64 characters."));
        if (req.LogMessage.Length > 256)
            return Results.BadRequest(ApiError.BadRequest("LogMessage must be ≤256 characters."));
        if (req.DelayMs < 0 || req.DelayMs > 3_600_000)
            return Results.BadRequest(ApiError.BadRequest("DelayMs must be 0..3600000."));
        return null;
    }

    // ─── DTO → domain mapping ─────────────────────────────────────────────────

    private static CustomEntry MapToCustomEntry(CreateCustomEntryRequest req) => new()
    {
        Name = req.Name,
        Address = req.Address,
        Type = req.Type,
        Value = req.Value,
        Continuous = req.Continuous,
        PeriodMs = req.PeriodMs,
        Monitor = req.Monitor,
        ReadPeriodMs = req.ReadPeriodMs,
        Area = req.Area,
        Trend = req.Trend
    };

    private static VisualNode MapToVisualNode(CreateSimulationNodeRequest req) => new()
    {
        Id = string.IsNullOrEmpty(req.Id) ? Guid.NewGuid().ToString() : req.Id,
        Name = req.Name,
        ElementType = req.ElementType,
        X = req.X,
        Y = req.Y,
        Width = req.Width,
        Height = req.Height,
        IsEnabled = req.IsEnabled,
        Waveform = req.Waveform,
        PeriodMs = req.PeriodMs,
        Amplitude = req.Amplitude,
        Offset = req.Offset
    };

    private static ScriptRule MapToScriptRule(CreateScriptRuleRequest req) => new()
    {
        Name = req.Name,
        Enabled = req.Enabled,
        ConditionType = req.ConditionType,
        TriggerAddress = req.TriggerAddress,
        TriggerArea = req.TriggerArea,
        TriggerOperator = req.TriggerOperator,
        TriggerValue = req.TriggerValue,
        ActionType = req.ActionType,
        ActionAddress = req.ActionAddress,
        ActionArea = req.ActionArea,
        ActionValue = req.ActionValue,
        DelayMs = req.DelayMs,
        LogMessage = req.LogMessage,
        OneTime = req.OneTime
    };
}

/// <summary>Generic success envelope returned by mutating endpoints.</summary>
internal record ApiOperationResult(bool Success);

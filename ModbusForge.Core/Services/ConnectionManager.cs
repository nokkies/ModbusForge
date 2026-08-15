using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;

namespace ModbusForge.Services;

public class ConnectionManager : IConnectionManager
{
    private static readonly string ProfilesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModbusForge",
        "connection-profiles.json");

    private readonly ILogger<ConnectionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IValidationService? _validationService;
    private readonly ICorrelationContext _correlationContext;
    private readonly IModbusAddressValidator _addressValidator;
    private readonly ConcurrentDictionary<string, IModbusService> _services = new();
    private ConnectionProfile? _activeProfile;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();

    public ConnectionProfile? ActiveProfile => _activeProfile;

    public IModbusService? ActiveService => _activeProfile != null ? GetServiceForProfile(_activeProfile) : null;

    public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
    public event EventHandler<ConnectionProfile>? ProfileConnected;
    public event EventHandler<ConnectionProfile>? ProfileDisconnected;

    public ConnectionManager(ILogger<ConnectionManager> logger, ILoggerFactory loggerFactory, IValidationService? validationService = null)
        : this(logger, loggerFactory, validationService, null, null)
    {
    }

    public ConnectionManager(ILogger<ConnectionManager> logger, ILoggerFactory loggerFactory, IValidationService? validationService, ICorrelationContext? correlationContext, IModbusAddressValidator? addressValidator)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _validationService = validationService;
        _correlationContext = correlationContext ?? new CorrelationContext();
        _addressValidator = addressValidator ?? new ModbusAddressValidator();
        LoadProfiles();

        // Add default profile if none exist
        if (Profiles.Count == 0)
        {
            AddProfile(new ConnectionProfile("Default", "127.0.0.1", 502, 1) { Mode = "Server" });
        }
    }

    public void AddProfile(ConnectionProfile profile)
    {
        Profiles.Add(profile);
        _logger.LogInformation("Added connection profile: {Name}", profile.Name);

        if (_activeProfile == null)
        {
            SetActiveProfile(profile);
        }
    }

    public void RemoveProfile(ConnectionProfile profile)
    {
        if (profile.IsConnected)
        {
            _ = DisconnectProfileAsync(profile);
        }

        if (_services.TryRemove(profile.Id, out var service))
        {
            service.Dispose();
        }

        Profiles.Remove(profile);
        _logger.LogInformation("Removed connection profile: {Name}", profile.Name);

        if (_activeProfile == profile)
        {
            SetActiveProfile(Profiles.Count > 0 ? Profiles[0] : null!);
        }
    }

    public void SetActiveProfile(ConnectionProfile profile)
    {
        if (_activeProfile != null)
        {
            _activeProfile.IsActive = false;
        }

        _activeProfile = profile;

        if (_activeProfile != null)
        {
            _activeProfile.IsActive = true;
        }

        ActiveProfileChanged?.Invoke(this, _activeProfile);
        _logger.LogInformation("Active profile changed to: {Name}", profile?.Name ?? "None");
    }

    public async Task<bool> ConnectProfileAsync(ConnectionProfile profile)
    {
        var correlationId = _correlationContext.StartNew();
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["Profile"] = profile.Name });

        try
        {
            // Validate serial settings before attempting to connect.
            if (profile.Transport != TransportType.Tcp)
            {
                var validation = _validationService?.ValidateSerialSettings(profile);
                if (validation is { IsValid: false })
                {
                    profile.IsConnected = false;
                    profile.Status = $"Invalid settings: {validation.ErrorMessage}";
                    _logger.LogWarning("Profile {Name} serial validation failed: {Error}", profile.Name, validation.ErrorMessage);
                    return false;
                }
            }

            var service = GetOrCreateService(profile);
            var success = await service.ConnectAsync(profile);

            if (success)
            {
                AttachConnectionLostHandler(service, profile);
                profile.IsConnected = true;
                profile.Status = "Connected";
                ProfileConnected?.Invoke(this, profile);
                _logger.LogInformation("Connected profile: {Name} to {Endpoint}",
                    profile.Name, service.BoundEndpoint);
            }
            else
            {
                profile.IsConnected = false;
                profile.Status = "Connection Failed";
            }

            return success;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            profile.IsConnected = false;
            profile.Status = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to connect profile: {Name}", profile.Name);
            return false;
        }
    }

    public async Task DisconnectProfileAsync(ConnectionProfile profile)
    {
        try
        {
            if (_services.TryGetValue(profile.Id, out var service))
            {
                service.ConnectionLost -= OnServiceConnectionLost;
                await service.DisconnectAsync();
            }

            profile.IsConnected = false;
            profile.Status = "Disconnected";
            ProfileDisconnected?.Invoke(this, profile);
            _logger.LogInformation("Disconnected profile: {Name}", profile.Name);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            _logger.LogError(ex, "Error disconnecting profile: {Name}", profile.Name);
        }
    }

    public async Task DisconnectAllAsync()
    {
        var connectedProfiles = Profiles.Where(p => p.IsConnected).ToList();
        var tasks = connectedProfiles.Select(DisconnectProfileAsync);
        await Task.WhenAll(tasks);
    }

    public IModbusService? GetServiceForProfile(ConnectionProfile profile)
    {
        return _services.TryGetValue(profile.Id, out var service) ? service : null;
    }

    private IModbusService GetOrCreateService(ConnectionProfile profile)
    {
        var transport = profile.Transport;

        if (_services.TryGetValue(profile.Id, out var existing))
        {
            if (MatchesTransport(existing, transport))
            {
                return existing;
            }

            _services.TryRemove(profile.Id, out _);
            existing.Dispose();
        }

        IModbusService service;

        if (profile.IsServerMode && transport == TransportType.Tcp)
        {
            service = new ModbusServerService(
                _loggerFactory.CreateLogger<ModbusServerService>(),
                null);
        }
        else
        {
            service = transport switch
            {
                TransportType.Rtu or TransportType.Ascii => new ModbusSerialService(
                    _loggerFactory.CreateLogger<ModbusSerialService>(),
                    null,
                    _validationService,
                    null,
                    _addressValidator,
                    transport),
                _ => new ModbusTcpService(_loggerFactory.CreateLogger<ModbusTcpService>(), null, null, _addressValidator)
            };
        }

        return _services.AddOrUpdate(profile.Id, service, (_, old) =>
        {
            if (old != service)
            {
                old.Dispose();
            }
            return service;
        })!;
    }

    private static bool MatchesTransport(IModbusService service, TransportType transport)
    {
        if (transport == TransportType.Tcp)
            return service is ModbusTcpService;

        return service is ModbusSerialService serial && serial.Transport == transport;
    }

    private void AttachConnectionLostHandler(IModbusService service, ConnectionProfile profile)
    {
        service.ConnectionLost -= OnServiceConnectionLost;
        service.ConnectionLost += OnServiceConnectionLost;
    }

    private void OnServiceConnectionLost(object? sender, EventArgs e)
    {
        // Raised from a worker thread by the service when its transport dies.
        if (sender is not IModbusService lostService)
        {
            return;
        }

        ConnectionProfile? profile = null;
        foreach (var candidate in Profiles)
        {
            if (_services.TryGetValue(candidate.Id, out var stored) && ReferenceEquals(stored, lostService))
            {
                profile = candidate;
                break;
            }
        }

        if (profile == null || !profile.IsConnected)
        {
            return;
        }

        _logger.LogWarning("Connection lost for profile {Name}", profile.Name);
        profile.IsConnected = false;
        profile.Status = "Connection lost";
        ProfileDisconnected?.Invoke(this, profile);
    }

    public void SaveProfiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(ProfilesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var data = new ProfilesData
            {
                ActiveProfileId = _activeProfile?.Id,
                Profiles = Profiles.Select(p => new ProfileDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    IpAddress = p.IpAddress,
                    Port = p.Port,
                    UnitId = p.UnitId,
                    Mode = p.Mode,
                    ServerUnitIds = p.ServerUnitIds,
                    Transport = p.Transport,
                    ComPort = p.ComPort,
                    BaudRate = p.BaudRate,
                    Parity = p.Parity,
                    DataBits = p.DataBits,
                    StopBits = p.StopBits,
                    RtsEnable = p.RtsEnable
                }).ToList()
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfilesFilePath, json);
            _logger.LogInformation("Saved {Count} connection profiles", Profiles.Count);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            _logger.LogError(ex, "Failed to save connection profiles");
        }
    }

    private static readonly JsonSerializerOptions _profileJsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Accept enum values written as strings (hand-edited files) as well as
        // the numeric form produced by the default serialization.
        Converters = { new JsonStringEnumConverter() }
    };

    public void LoadProfiles()
    {
        try
        {
            if (!File.Exists(ProfilesFilePath))
            {
                return;
            }

            var json = File.ReadAllText(ProfilesFilePath);
            var data = JsonSerializer.Deserialize<ProfilesData>(json, _profileJsonOptions);

            if (data?.Profiles != null)
            {
                Profiles.Clear();
                foreach (var dto in data.Profiles)
                {
                    var profile = new ConnectionProfile
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        IpAddress = dto.IpAddress,
                        Port = dto.Port,
                        UnitId = dto.UnitId,
                        Mode = string.IsNullOrWhiteSpace(dto.Mode) ? "Server" : dto.Mode,
                        ServerUnitIds = string.IsNullOrWhiteSpace(dto.ServerUnitIds) ? "1" : dto.ServerUnitIds,
                        Transport = dto.Transport,
                        ComPort = dto.ComPort,
                        BaudRate = dto.BaudRate,
                        Parity = dto.Parity,
                        DataBits = dto.DataBits,
                        StopBits = dto.StopBits,
                        RtsEnable = dto.RtsEnable
                    };
                    Profiles.Add(profile);

                    if (dto.Id == data.ActiveProfileId)
                    {
                        SetActiveProfile(profile);
                    }
                }
                _logger.LogInformation("Loaded {Count} connection profiles", Profiles.Count);
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            _logger.LogError(ex, "Failed to load connection profiles");
        }
    }

    private class ProfilesData
    {
        public string? ActiveProfileId { get; set; }
        public List<ProfileDto> Profiles { get; set; } = new();
    }

    private class ProfileDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public byte UnitId { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string ServerUnitIds { get; set; } = string.Empty;
        public TransportType Transport { get; set; } = TransportType.Tcp;
        public string ComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public bool RtsEnable { get; set; }
    }
}

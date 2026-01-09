using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private readonly ConcurrentDictionary<string, ModbusTcpService> _services = new();
    private ConnectionProfile? _activeProfile;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
    
    public ConnectionProfile? ActiveProfile => _activeProfile;
    
    public IModbusService? ActiveService => _activeProfile != null ? GetServiceForProfile(_activeProfile) : null;

    public event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
    public event EventHandler<ConnectionProfile>? ProfileConnected;
    public event EventHandler<ConnectionProfile>? ProfileDisconnected;

    public ConnectionManager(ILogger<ConnectionManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        LoadProfiles();
        
        // Add default profile if none exist
        if (Profiles.Count == 0)
        {
            AddProfile(new ConnectionProfile("Default", "127.0.0.1", 502, 1));
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
        try
        {
            var service = GetOrCreateService(profile);
            var success = await service.ConnectAsync(profile.IpAddress, profile.Port);
            
            if (success)
            {
                profile.IsConnected = true;
                profile.Status = "Connected";
                ProfileConnected?.Invoke(this, profile);
                _logger.LogInformation("Connected profile: {Name} to {Ip}:{Port}", 
                    profile.Name, profile.IpAddress, profile.Port);
            }
            else
            {
                profile.IsConnected = false;
                profile.Status = "Connection Failed";
            }

            return success;
        }
        catch (Exception ex)
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
                await service.DisconnectAsync();
            }

            profile.IsConnected = false;
            profile.Status = "Disconnected";
            ProfileDisconnected?.Invoke(this, profile);
            _logger.LogInformation("Disconnected profile: {Name}", profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting profile: {Name}", profile.Name);
        }
    }

    public async Task DisconnectAllAsync()
    {
        foreach (var profile in Profiles)
        {
            if (profile.IsConnected)
            {
                await DisconnectProfileAsync(profile);
            }
        }
    }

    public IModbusService? GetServiceForProfile(ConnectionProfile profile)
    {
        return _services.TryGetValue(profile.Id, out var service) ? service : null;
    }

    private ModbusTcpService GetOrCreateService(ConnectionProfile profile)
    {
        return _services.GetOrAdd(profile.Id, _ => 
            new ModbusTcpService(_loggerFactory.CreateLogger<ModbusTcpService>()));
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
                    UnitId = p.UnitId
                }).ToList()
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfilesFilePath, json);
            _logger.LogInformation("Saved {Count} connection profiles", Profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save connection profiles");
        }
    }

    public void LoadProfiles()
    {
        try
        {
            if (!File.Exists(ProfilesFilePath))
            {
                return;
            }

            var json = File.ReadAllText(ProfilesFilePath);
            var data = JsonSerializer.Deserialize<ProfilesData>(json);
            
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
                        UnitId = dto.UnitId
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
        catch (Exception ex)
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
    }
}

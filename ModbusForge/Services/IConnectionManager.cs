using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.Services;

public interface IConnectionManager
{
    ObservableCollection<ConnectionProfile> Profiles { get; }
    ConnectionProfile? ActiveProfile { get; }
    IModbusService? ActiveService { get; }
    
    event EventHandler<ConnectionProfile?>? ActiveProfileChanged;
    event EventHandler<ConnectionProfile>? ProfileConnected;
    event EventHandler<ConnectionProfile>? ProfileDisconnected;
    
    void AddProfile(ConnectionProfile profile);
    void RemoveProfile(ConnectionProfile profile);
    void SetActiveProfile(ConnectionProfile profile);
    
    Task<bool> ConnectProfileAsync(ConnectionProfile profile);
    Task DisconnectProfileAsync(ConnectionProfile profile);
    Task DisconnectAllAsync();
    
    IModbusService? GetServiceForProfile(ConnectionProfile profile);
    
    void SaveProfiles();
    void LoadProfiles();
}

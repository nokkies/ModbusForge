using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models;

public partial class ConnectionProfile : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = "New Connection";

    [ObservableProperty]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    private int _port = 502;

    [ObservableProperty]
    private byte _unitId = 1;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _status = "Disconnected";

    [ObservableProperty]
    private bool _isActive;

    public string DisplayName => $"{Name} ({IpAddress}:{Port})";

    public ConnectionProfile() { }

    public ConnectionProfile(string name, string ipAddress, int port, byte unitId)
    {
        Name = name;
        IpAddress = ipAddress;
        Port = port;
        UnitId = unitId;
    }

    public ConnectionProfile Clone()
    {
        return new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name + " (Copy)",
            IpAddress = IpAddress,
            Port = Port,
            UnitId = UnitId
        };
    }
}

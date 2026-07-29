using System;
using System.IO.Ports;
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

    [ObservableProperty]
    private TransportType _transport = TransportType.Tcp;

    // Serial settings
    [ObservableProperty]
    private string _comPort = "COM1";

    [ObservableProperty]
    private int _baudRate = 9600;

    [ObservableProperty]
    private Parity _parity = Parity.None;

    [ObservableProperty]
    private int _dataBits = 8;

    [ObservableProperty]
    private StopBits _stopBits = StopBits.One;

    [ObservableProperty]
    private bool _rtsEnable;

    [ObservableProperty]
    private bool _enableRtsToggle;

    [ObservableProperty]
    private int _preTxDelayMs;

    [ObservableProperty]
    private int _postTxDelayMs;

    public string DisplayName => Transport switch
    {
        TransportType.Tcp => $"{Name} ({IpAddress}:{Port})",
        _ => $"{Name} ({ComPort} {BaudRate} {Transport})"
    };

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
            UnitId = UnitId,
            Transport = Transport,
            ComPort = ComPort,
            BaudRate = BaudRate,
            Parity = Parity,
            DataBits = DataBits,
            StopBits = StopBits,
            RtsEnable = RtsEnable,
            EnableRtsToggle = EnableRtsToggle,
            PreTxDelayMs = PreTxDelayMs,
            PostTxDelayMs = PostTxDelayMs
        };
    }
}

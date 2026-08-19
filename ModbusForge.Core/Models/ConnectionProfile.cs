using System;
using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models;

public partial class ConnectionProfile : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = "New Connection";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
    private int _port = 502;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
    private byte _unitId = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionLost))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionLost))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private string _status = "Disconnected";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
    private TransportType _transport = TransportType.Tcp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerMode))]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
    private string _mode = "Client";

    [ObservableProperty]
    private string _serverUnitIds = "1";

    public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);

    // Serial settings
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
    private string _comPort = "COM1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(EndpointDescription))]
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

    /// <summary>
    /// Short endpoint summary for secondary UI text (e.g. the dashboard profile list).
    /// </summary>
    public string EndpointDescription => Transport switch
    {
        TransportType.Tcp => $"{IpAddress}:{Port} · {Mode} · Unit {UnitId}",
        _ => $"{ComPort} @ {BaudRate} · {Mode}"
    };

    /// <summary>
    /// True when the transport died and the loss was detected (as opposed to a
    /// deliberate disconnect or a failed connection attempt).
    /// </summary>
    public bool HasConnectionLost => !IsConnected && Status == "Connection lost";

    /// <summary>
    /// True when the profile is simply not connected (no loss detected).
    /// </summary>
    public bool IsIdle => !IsConnected && !HasConnectionLost;

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
            Mode = Mode,
            ServerUnitIds = ServerUnitIds,
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

using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models;

public partial class ConnectionProfile : ObservableObject, IDataErrorInfo
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerMode))]
    private string _mode = "Client";

    [ObservableProperty]
    private string _serverUnitIds = "1";

    public bool IsServerMode => string.Equals(Mode, "Server", StringComparison.OrdinalIgnoreCase);

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

    // ---- IDataErrorInfo: model-level validation for the profile editor ----

    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            switch (columnName)
            {
                case nameof(IpAddress):
                    if (string.IsNullOrWhiteSpace(IpAddress))
                        return "IP address is required.";

                    var host = IpAddress.Trim();
                    if (IPAddress.TryParse(host, out _))
                        return string.Empty;
                    // DNS names are a legal target for a TCP connection.
                    if (IsValidHostName(host))
                        return string.Empty;
                    return "Enter a valid IP address or host name.";

                case nameof(Port):
                    return Port is >= 1 and <= 65535
                        ? string.Empty
                        : "Port must be between 1 and 65535.";

                case nameof(UnitId):
                    return UnitId is >= 1 and <= 247
                        ? string.Empty
                        : "Unit ID must be between 1 and 247.";

                case nameof(ServerUnitIds):
                    return ValidateServerUnitIds(ServerUnitIds);

                case nameof(BaudRate):
                    return BaudRate is >= 1 and <= 1000000
                        ? string.Empty
                        : "Baud rate must be between 1 and 1000000.";

                default:
                    return string.Empty;
            }
        }
    }

    private static readonly Regex HostNamePattern = new(
        @"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled);

    private static bool IsValidHostName(string host)
    {
        if (host.Length is not (> 0 and <= 253) || host.Contains(' ', StringComparison.Ordinal))
            return false;

        // Names without any letter (e.g. "123", "999.1.1.1") are not hostnames
        // (RFC 952: labels must not be all-numeric).
        if (!host.Any(char.IsLetter))
            return false;

        return HostNamePattern.IsMatch(host);
    }

    /// <summary>
    /// Validates the server unit ID list (e.g. "1, 2, 5-10"): comma/space separated
    /// single IDs or ranges, each value in 1..247, at least one entry.
    /// </summary>
    public static string ValidateServerUnitIds(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "At least one unit ID is required.";

        var tokens = value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return "At least one unit ID is required.";

        foreach (var token in tokens)
        {
            if (token.Contains('-'))
            {
                var parts = token.Split('-', 2);
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], out var from) ||
                    !int.TryParse(parts[1], out var to) ||
                    from < 1 || from > 247 || to < 1 || to > 247 || from > to)
                {
                    return $"'{token}' is not a valid range (e.g. 5-10).";
                }
            }
            else if (!int.TryParse(token, out var id) || id < 1 || id > 247)
            {
                return $"'{token}' is not a valid unit ID (1-247).";
            }
        }

        return string.Empty;
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

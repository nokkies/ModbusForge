using System;

namespace ModbusForge.Configuration
{
    public class ServerSettings
    {
        public string Mode { get; set; } = "Client"; // "Client" or "Server"
        public int DefaultPort { get; set; } = 502;
        public byte DefaultUnitId { get; set; } = 1; // default to Unit ID 1
        public int MaxConnections { get; set; } = 10;
    }
}

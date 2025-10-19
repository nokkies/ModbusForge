using System.Collections.Generic;

namespace ModbusForge.Models
{
    public class AppConfiguration
    {
        public string? Mode { get; set; }
        public string? ServerAddress { get; set; }
        public int Port { get; set; }
        public byte UnitId { get; set; }
        public List<CustomEntry>? CustomEntries { get; set; }
    }
}

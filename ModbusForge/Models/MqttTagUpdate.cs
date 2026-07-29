using System;

namespace ModbusForge.Models
{
    /// <summary>
    /// A single tag value update to be published over MQTT.
    /// </summary>
    public class MqttTagUpdate
    {
        public byte UnitId { get; set; }
        public string TagName { get; set; } = string.Empty;
        public PlcArea Area { get; set; }
        public int Address { get; set; }
        public object? Value { get; set; }
        public string? Unit { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

using System.ComponentModel.DataAnnotations;

namespace ModbusForge.Models
{
    /// <summary>
    /// MQTT publisher settings for ModbusForge.
    /// </summary>
    public class MqttSettings
    {
        public bool Enabled { get; set; } = false;

        [Required]
        public string BrokerHost { get; set; } = "localhost";

        public int BrokerPort { get; set; } = 1883;

        public string ClientId { get; set; } = "ModbusForge";

        public string? Username { get; set; }

        public string? Password { get; set; }

        /// <summary>
        /// Topic template. Supports placeholders {UnitId}, {Tag}, and {Area}.
        /// Default: modbusforge/{UnitId}/{Tag}
        /// </summary>
        public string TopicTemplate { get; set; } = "modbusforge/{UnitId}/{Tag}";

        public int QualityOfService { get; set; } = 0;

        public bool RetainMessages { get; set; } = false;

        /// <summary>
        /// Publish period in milliseconds. Set to 0 to publish on every value change (when a change-source is wired).
        /// </summary>
        public int PublishPeriodMs { get; set; } = 1000;
    }
}

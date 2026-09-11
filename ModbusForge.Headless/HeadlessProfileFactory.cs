using System;
using System.IO.Ports;
using Microsoft.Extensions.Configuration;
using ModbusForge.Models;

namespace ModbusForge.Headless
{
    /// <summary>
    /// Builds a <see cref="ConnectionProfile"/> and other headless configuration from
    /// application configuration and command-line arguments.
    /// </summary>
    internal static class HeadlessProfileFactory
    {
        public static ConnectionProfile CreateConnectionProfile(IConfiguration configuration)
        {
            var host = configuration["Connection:Host"] ?? "127.0.0.1";
            var port = configuration.GetValue<int?>("Connection:Port") ?? 502;
            var unitId = configuration.GetValue<byte?>("Connection:UnitId") ?? 1;
            var name = configuration["Connection:Name"] ?? "Headless";

            var profile = new ConnectionProfile(name, host, port, unitId)
            {
                Mode = configuration["Connection:Mode"] ?? "Client",
                Transport = ParseTransport(configuration["Connection:Transport"]),
                ComPort = configuration["Connection:ComPort"] ?? "COM1",
                BaudRate = configuration.GetValue<int?>("Connection:BaudRate") ?? 9600,
                Parity = configuration.GetValue<Parity?>("Connection:Parity") ?? Parity.None,
                DataBits = configuration.GetValue<int?>("Connection:DataBits") ?? 8,
                StopBits = configuration.GetValue<StopBits?>("Connection:StopBits") ?? StopBits.One,
                RtsEnable = configuration.GetValue<bool?>("Connection:RtsEnable") ?? false,
                PreTxDelayMs = configuration.GetValue<int?>("Connection:PreTxDelayMs") ?? 0,
                PostTxDelayMs = configuration.GetValue<int?>("Connection:PostTxDelayMs") ?? 0,
            };

            return profile;
        }

        public static MqttSettings CreateMqttSettings(IConfiguration configuration)
        {
            var settings = configuration.GetSection("Mqtt").Get<MqttSettings>() ?? new MqttSettings();

            // Headless deployments identify as ModbusForge-Headless so their MQTT traffic can be
            // distinguished from the desktop app, unless the user configured an explicit ClientId.
            if (string.IsNullOrWhiteSpace(configuration["Mqtt:ClientId"]))
            {
                settings.ClientId = "ModbusForge-Headless";
            }

            return settings;
        }

        private static TransportType ParseTransport(string? value)
        {
            if (string.Equals(value, "Serial", StringComparison.OrdinalIgnoreCase))
            {
                return TransportType.Rtu;
            }

            if (Enum.TryParse<TransportType>(value, true, out var transport))
            {
                return transport;
            }

            return TransportType.Tcp;
        }
    }
}

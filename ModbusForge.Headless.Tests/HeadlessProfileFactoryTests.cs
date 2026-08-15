using System.Collections.Generic;
using System.IO.Ports;
using Microsoft.Extensions.Configuration;
using ModbusForge.Models;
using ModbusForge.Headless;

namespace ModbusForge.Headless.Tests
{
    public class HeadlessProfileFactoryTests
    {
        private static IConfiguration BuildConfig(IDictionary<string, string?>? values = null)
            => new ConfigurationBuilder()
                .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
                .Build();

        [Theory]
        [InlineData("Tcp", TransportType.Tcp)]
        [InlineData("rtu", TransportType.Rtu)]
        [InlineData("Serial", TransportType.Rtu)]
        [InlineData("ASCII", TransportType.Ascii)]
        [InlineData(null, TransportType.Tcp)]
        public void CreateConnectionProfile_ParsesTransport(string? value, TransportType expected)
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Connection:Transport"] = value
            });

            var profile = HeadlessProfileFactory.CreateConnectionProfile(config);

            Assert.Equal(expected, profile.Transport);
        }

        [Fact]
        public void CreateConnectionProfile_UnknownTransport_FallsBackToTcp()
        {
            // Documented behavior: an unrecognized transport value falls back to Tcp.
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Connection:Transport"] = "Rtuu"
            });

            Assert.Equal(TransportType.Tcp, HeadlessProfileFactory.CreateConnectionProfile(config).Transport);
        }

        [Fact]
        public void CreateConnectionProfile_WithoutConfig_UsesSaneDefaults()
        {
            var profile = HeadlessProfileFactory.CreateConnectionProfile(BuildConfig());

            Assert.Equal("127.0.0.1", profile.IpAddress);
            Assert.Equal(502, profile.Port);
            Assert.Equal((byte)1, profile.UnitId);
            Assert.Equal(TransportType.Tcp, profile.Transport);
            Assert.Equal("COM1", profile.ComPort);
            Assert.Equal(9600, profile.BaudRate);
            Assert.Equal(Parity.None, profile.Parity);
            Assert.Equal(8, profile.DataBits);
            Assert.Equal(StopBits.One, profile.StopBits);
            Assert.False(profile.RtsEnable);
            Assert.Equal(0, profile.PreTxDelayMs);
            Assert.Equal(0, profile.PostTxDelayMs);
        }

        [Fact]
        public void CreateConnectionProfile_ReadsOverrides()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Connection:Host"] = "10.0.0.5",
                ["Connection:Port"] = "1502",
                ["Connection:UnitId"] = "7",
                ["Connection:BaudRate"] = "115200",
            });

            var profile = HeadlessProfileFactory.CreateConnectionProfile(config);

            Assert.Equal("10.0.0.5", profile.IpAddress);
            Assert.Equal(1502, profile.Port);
            Assert.Equal((byte)7, profile.UnitId);
            Assert.Equal(115200, profile.BaudRate);
        }

        [Fact]
        public void CreateMqttSettings_WithoutConfig_UsesHeadlessClientId()
        {
            var settings = HeadlessProfileFactory.CreateMqttSettings(BuildConfig());

            Assert.Equal("ModbusForge-Headless", settings.ClientId);
        }

        [Fact]
        public void CreateMqttSettings_ExplicitClientId_Wins()
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Mqtt:ClientId"] = "my-poller",
                ["Mqtt:BrokerHost"] = "broker.local",
            });

            var settings = HeadlessProfileFactory.CreateMqttSettings(config);

            Assert.Equal("my-poller", settings.ClientId);
            Assert.Equal("broker.local", settings.BrokerHost);
        }
    }
}

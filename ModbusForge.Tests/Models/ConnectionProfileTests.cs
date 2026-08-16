using System.Collections.Generic;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Models
{
    public class ConnectionProfileTests
    {
        [Fact]
        public void EndpointDescription_Tcp_IncludesEndpointModeAndUnit()
        {
            var profile = new ConnectionProfile("PLC", "192.168.1.10", 502, 3);

            Assert.Equal("192.168.1.10:502 · Client · Unit 3", profile.EndpointDescription);
        }

        [Theory]
        [InlineData(TransportType.Rtu)]
        [InlineData(TransportType.Ascii)]
        public void EndpointDescription_SerialTransports_IncludePortBaudAndMode(TransportType transport)
        {
            var profile = new ConnectionProfile("Meter", "127.0.0.1", 502, 1)
            {
                Transport = transport,
                ComPort = "COM4",
                BaudRate = 19200,
                Mode = "Client"
            };

            Assert.Equal("COM4 @ 19200 · Client", profile.EndpointDescription);
        }

        // A connected profile is never "idle" (the idle dot means "not connected");
        // a disconnected profile is "lost" only when the loss was detected, idle otherwise.
        [Theory]
        [InlineData(true, "Connected", false, false)]
        [InlineData(true, "Connection lost", false, false)] // transient: still connected while the loss event propagates
        [InlineData(false, "Disconnected", false, true)]
        [InlineData(false, "Connection Failed", false, true)]
        [InlineData(false, "Invalid settings: Port required", false, true)]
        [InlineData(false, "Connection lost", true, false)]
        public void LostAndIdleStates_AreMutuallyExclusiveAndComplete(
            bool isConnected, string status, bool expectLost, bool expectIdle)
        {
            var profile = new ConnectionProfile();
            profile.IsConnected = isConnected;
            profile.Status = status;

            Assert.Equal(expectLost, profile.HasConnectionLost);
            Assert.Equal(expectIdle, profile.IsIdle);
        }

        [Fact]
        public void StatusChange_RaisesPropertyChangedForDerivedFlags()
        {
            var profile = new ConnectionProfile();
            var raised = new List<string>();
            profile.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            profile.Status = "Connection lost";

            Assert.Contains(nameof(ConnectionProfile.HasConnectionLost), raised);
            Assert.Contains(nameof(ConnectionProfile.IsIdle), raised);
        }

        [Fact]
        public void ConnectedChange_RaisesPropertyChangedForDerivedFlags()
        {
            var profile = new ConnectionProfile { Status = "Connection lost" };
            var raised = new List<string>();
            profile.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            profile.IsConnected = true;

            Assert.Contains(nameof(ConnectionProfile.HasConnectionLost), raised);
            Assert.Contains(nameof(ConnectionProfile.IsIdle), raised);
        }

        [Fact]
        public void AddressChange_RaisesPropertyChangedForDisplayAndEndpoint()
        {
            var profile = new ConnectionProfile("PLC", "127.0.0.1", 502, 1);
            var raised = new List<string>();
            profile.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            profile.IpAddress = "192.168.86.20";

            Assert.Contains(nameof(ConnectionProfile.DisplayName), raised);
            Assert.Contains(nameof(ConnectionProfile.EndpointDescription), raised);
        }

        [Fact]
        public void NameChange_RaisesPropertyChangedForDisplayName()
        {
            var profile = new ConnectionProfile();
            var raised = new List<string>();
            profile.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

            profile.Name = "Renamed";

            Assert.Contains(nameof(ConnectionProfile.DisplayName), raised);
        }
    }
}

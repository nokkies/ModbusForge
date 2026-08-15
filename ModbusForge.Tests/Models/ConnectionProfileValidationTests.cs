using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Models
{
    public class ConnectionProfileValidationTests
    {
        [Theory]
        [InlineData("127.0.0.1", "")]
        [InlineData("10.0.0.254", "")]
        [InlineData("192.168.1.1", "")]
        [InlineData("plc-01.lan", "")]
        [InlineData("myhost", "")]
        [InlineData("123", "")] // legal one-part IPv4 literal (0.0.0.123)
        [InlineData("999.1.1.1", "Enter a valid IP address or host name.")]
        [InlineData("123.456.7.8", "Enter a valid IP address or host name.")]
        [InlineData("", "IP address is required.")]
        [InlineData("   ", "IP address is required.")]
        [InlineData("999.1.1.1", "Enter a valid IP address or host name.")]
        [InlineData("127.0.0.1 extra", "Enter a valid IP address or host name.")]
        [InlineData("host name", "Enter a valid IP address or host name.")]
        public void IpAddress_Validation(string ip, string expected)
        {
            var profile = new ConnectionProfile { IpAddress = ip };
            Assert.Equal(expected, profile[nameof(ConnectionProfile.IpAddress)]);
        }

        [Theory]
        [InlineData(502, "")]
        [InlineData(1, "")]
        [InlineData(65535, "")]
        [InlineData(0, "Port must be between 1 and 65535.")]
        [InlineData(-1, "Port must be between 1 and 65535.")]
        [InlineData(70000, "Port must be between 1 and 65535.")]
        public void Port_Validation(int port, string expected)
        {
            var profile = new ConnectionProfile { Port = port };
            Assert.Equal(expected, profile[nameof(ConnectionProfile.Port)]);
        }

        [Theory]
        [InlineData(1, "")]
        [InlineData(247, "")]
        [InlineData(0, "Unit ID must be between 1 and 247.")]
        [InlineData(248, "Unit ID must be between 1 and 247.")]
        public void UnitId_Validation(byte unitId, string expected)
        {
            var profile = new ConnectionProfile { UnitId = unitId };
            Assert.Equal(expected, profile[nameof(ConnectionProfile.UnitId)]);
        }

        [Theory]
        [InlineData("1", "")]
        [InlineData("1, 2, 5-10", "")]
        [InlineData("1;2;3", "")]
        [InlineData("5-10", "")]
        [InlineData("247", "")]
        [InlineData("", "At least one unit ID is required.")]
        [InlineData("   ", "At least one unit ID is required.")]
        [InlineData("0", "'0' is not a valid unit ID (1-247).")]
        [InlineData("248", "'248' is not a valid unit ID (1-247).")]
        [InlineData("abc", "'abc' is not a valid unit ID (1-247).")]
        [InlineData("10-5", "'10-5' is not a valid range (e.g. 5-10).")]
        [InlineData("1-248", "'1-248' is not a valid range (e.g. 5-10).")]
        [InlineData("1, 0", "'0' is not a valid unit ID (1-247).")]
        public void ServerUnitIds_Validation(string value, string expected)
        {
            var profile = new ConnectionProfile { ServerUnitIds = value };
            Assert.Equal(expected, profile[nameof(ConnectionProfile.ServerUnitIds)]);
        }

        [Theory]
        [InlineData(9600, "")]
        [InlineData(115200, "")]
        [InlineData(0, "Baud rate must be between 1 and 1000000.")]
        [InlineData(2000000, "Baud rate must be between 1 and 1000000.")]
        public void BaudRate_Validation(int baud, string expected)
        {
            var profile = new ConnectionProfile { BaudRate = baud };
            Assert.Equal(expected, profile[nameof(ConnectionProfile.BaudRate)]);
        }
    }
}

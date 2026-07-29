using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class MqttGatewayServiceTests
    {
        [Fact]
        public void BuildTopic_ReplacesUnitTagAndAreaPlaceholders()
        {
            var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { TopicTemplate = "modbusforge/{UnitId}/{Tag}" });

            var topic = service.BuildTopic(new MqttTagUpdate
            {
                UnitId = 7,
                TagName = "Flow",
                Area = PlcArea.HoldingRegister,
                Address = 100,
            });

            Assert.Equal("modbusforge/7/Flow", topic);
        }

        [Fact]
        public void BuildTopic_IsCaseInsensitive()
        {
            var service = new MqttGatewayService(NullLogger<MqttGatewayService>.Instance);
            service.ApplySettings(new MqttSettings { TopicTemplate = "{unitid}/{tag}/{area}/{address}" });

            var topic = service.BuildTopic(new MqttTagUpdate
            {
                UnitId = 2,
                TagName = "Run",
                Area = PlcArea.Coil,
                Address = 5,
            });

            Assert.Equal("2/Run/Coil/5", topic);
        }
    }
}

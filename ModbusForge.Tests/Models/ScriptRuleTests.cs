using Xunit;
using ModbusForge.Models;

namespace ModbusForge.Tests.Models
{
    public class ScriptRuleTests
    {
        [Fact]
        public void Clone_ReturnsNewInstanceWithSamePropertyValues()
        {
            // Arrange
            var original = new ScriptRule
            {
                Name = "Test Rule",
                Enabled = false,
                ConditionType = "CoilsState",
                TriggerAddress = 10,
                TriggerArea = "Coil",
                TriggerOperator = "NotEquals",
                TriggerValue = "1",
                ActionType = "SetCoil",
                ActionAddress = 20,
                ActionArea = "Coil",
                ActionValue = "0",
                DelayMs = 500,
                LogMessage = "Test triggered",
                OneTime = true,
                Triggered = true
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotNull(clone);
            Assert.NotSame(original, clone);

            Assert.Equal(original.Name, clone.Name);
            Assert.Equal(original.Enabled, clone.Enabled);
            Assert.Equal(original.ConditionType, clone.ConditionType);
            Assert.Equal(original.TriggerAddress, clone.TriggerAddress);
            Assert.Equal(original.TriggerArea, clone.TriggerArea);
            Assert.Equal(original.TriggerOperator, clone.TriggerOperator);
            Assert.Equal(original.TriggerValue, clone.TriggerValue);
            Assert.Equal(original.ActionType, clone.ActionType);
            Assert.Equal(original.ActionAddress, clone.ActionAddress);
            Assert.Equal(original.ActionArea, clone.ActionArea);
            Assert.Equal(original.ActionValue, clone.ActionValue);
            Assert.Equal(original.DelayMs, clone.DelayMs);
            Assert.Equal(original.LogMessage, clone.LogMessage);
            Assert.Equal(original.OneTime, clone.OneTime);
            Assert.Equal(original.Triggered, clone.Triggered);
        }

        [Fact]
        public void GetDescription_ReturnsFormattedString()
        {
            // Arrange
            var rule = new ScriptRule
            {
                TriggerArea = "Coil",
                TriggerAddress = 5,
                TriggerOperator = "Equals",
                TriggerValue = "True",
                ActionType = "SetRegister",
                ActionArea = "HoldingRegister",
                ActionAddress = 10,
                ActionValue = "123"
            };

            // Act
            var description = rule.GetDescription();

            // Assert
            Assert.Equal("IF Coil[5] Equals True THEN SetRegister HoldingRegister[10] = 123", description);
        }
    }
}

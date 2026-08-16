using System.ComponentModel;
using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Models;

public class ScriptRuleTests
{
    [Fact]
    public void Description_ReflectsAllContributingFields()
    {
        var rule = new ScriptRule
        {
            TriggerArea = "InputRegister",
            TriggerAddress = 5,
            TriggerOperator = "GreaterThan",
            TriggerValue = "10",
            ActionType = "SetCoil",
            ActionArea = "Coil",
            ActionAddress = 9,
            ActionValue = "true"
        };

        Assert.Equal("IF InputRegister[5] GreaterThan 10 THEN SetCoil Coil[9] = true", rule.Description);
        Assert.Equal(rule.Description, rule.GetDescription());
    }

    [Theory]
    [InlineData("TriggerArea")]
    [InlineData("TriggerAddress")]
    [InlineData("TriggerOperator")]
    [InlineData("TriggerValue")]
    [InlineData("ActionType")]
    [InlineData("ActionArea")]
    [InlineData("ActionAddress")]
    [InlineData("ActionValue")]
    public void ChangingAContributingField_RaisesDescriptionChanged(string propertyName)
    {
        var rule = new ScriptRule();
        var raised = new System.Collections.Generic.List<string>();
        rule.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        switch (propertyName)
        {
            case "TriggerArea": rule.TriggerArea = "Coil"; break;
            case "TriggerAddress": rule.TriggerAddress = 42; break;
            case "TriggerOperator": rule.TriggerOperator = "LessThan"; break;
            case "TriggerValue": rule.TriggerValue = "7"; break;
            case "ActionType": rule.ActionType = "LogMessage"; break;
            case "ActionArea": rule.ActionArea = "InputRegister"; break;
            case "ActionAddress": rule.ActionAddress = 3; break;
            case "ActionValue": rule.ActionValue = "5"; break;
        }

        Assert.Contains(nameof(ScriptRule.Description), raised);
    }

    [Fact]
    public void Clone_CopiesConfiguration_ButNotRuntimeState()
    {
        var rule = new ScriptRule
        {
            Name = "Alpha",
            TriggerValue = "11",
            OneTime = true,
            Triggered = true,
            LastTriggeredAt = System.DateTime.Now
        };

        var clone = rule.Clone();

        Assert.Equal("Alpha", clone.Name);
        Assert.Equal("11", clone.TriggerValue);
        Assert.True(clone.OneTime);
        Assert.False(clone.Triggered);
        Assert.Null(clone.LastTriggeredAt);
        Assert.NotSame(rule, clone);
    }

    [Fact]
    public void Clone_DoesNotShareState_WithOriginal()
    {
        var rule = new ScriptRule { Name = "Original" };
        var clone = rule.Clone();

        clone.Name = "Renamed";

        Assert.Equal("Original", rule.Name);
    }
}

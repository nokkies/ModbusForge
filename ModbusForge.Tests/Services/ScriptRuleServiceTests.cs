using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

public class ScriptRuleServiceTests
{
    private readonly Mock<ILogger<ScriptRuleService>> _mockLogger;
    private readonly Mock<IConnectionManager> _mockConnectionManager;
    private readonly Mock<IModbusService> _mockService;
    private readonly Mock<IConsoleLoggerService> _mockConsole;
    private readonly ConnectionProfile _profile;

    public ScriptRuleServiceTests()
    {
        _mockLogger = new Mock<ILogger<ScriptRuleService>>();
        _mockConnectionManager = new Mock<IConnectionManager>();
        _mockService = new Mock<IModbusService>();
        _mockConsole = new Mock<IConsoleLoggerService>();
        _profile = new ConnectionProfile { Name = "Test", UnitId = 5 };

        _mockService.SetupGet(s => s.IsConnected).Returns(true);
        _mockConnectionManager.SetupGet(c => c.ActiveService).Returns(_mockService.Object);
        _mockConnectionManager.SetupGet(c => c.ActiveProfile).Returns(_profile);
    }

    private ScriptRuleService CreateService()
    {
        // Long evaluation interval: the service's background timer must not
        // tick during a test (it would re-evaluate against the mocks and make
        // write-count assertions flaky).
        return new ScriptRuleService(_mockLogger.Object, _mockConnectionManager.Object, _mockConsole.Object, TimeSpan.FromMinutes(5));
    }

    private static ScriptRule MakeRule(string actionType = "SetRegister", string actionValue = "7")
    {
        return new ScriptRule
        {
            Name = "Rule",
            Enabled = true,
            TriggerArea = "HoldingRegister",
            TriggerAddress = 10,
            TriggerOperator = "Equals",
            TriggerValue = "42",
            ActionType = actionType,
            ActionArea = "HoldingRegister",
            ActionAddress = 20,
            ActionValue = actionValue,
            DelayMs = 0
        };
    }

    private static ScriptRule Vary(Action<ScriptRule> modify)
    {
        var rule = MakeRule();
        modify(rule);
        return rule;
    }

    [Fact]
    public async Task Evaluate_NoActiveService_DoesNotTouchModbus()
    {
        _mockConnectionManager.SetupGet(c => c.ActiveService).Returns((IModbusService?)null);
        using var service = CreateService();
        service.AddRule(MakeRule());

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_DisconnectedService_DoesNotTouchModbus()
    {
        _mockService.SetupGet(s => s.IsConnected).Returns(false);
        using var service = CreateService();
        service.AddRule(MakeRule());

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_ConditionMet_TriggersSetRegisterAction()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        service.AddRule(MakeRule());

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), 20, (ushort)7), Times.Once);
        _mockConsole.Verify(c => c.Log(It.Is<string>(m => m.Contains("Rule triggered"))), Times.Once);
    }

    [Fact]
    public async Task Evaluate_ConditionMet_RecordsLastTriggeredAt()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        var rule = MakeRule();
        service.AddRule(rule);

        Assert.Null(rule.LastTriggeredAt);

        var before = DateTime.Now;
        await service.EvaluateRulesAsync();
        var after = DateTime.Now;

        Assert.NotNull(rule.LastTriggeredAt);
        Assert.InRange(rule.LastTriggeredAt!.Value, before, after);

        // A missed condition does not erase the stamp.
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 0 });
        var previous = rule.LastTriggeredAt;
        await service.EvaluateRulesAsync();
        Assert.Equal(previous, rule.LastTriggeredAt);
    }

    [Fact]
    public async Task Evaluate_UsesActiveProfileUnitId()
    {
        // Regression: rules used to read/write the static server default unit
        // id instead of the active connection's unit id.
        _profile.UnitId = 7;
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        service.AddRule(MakeRule());

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.ReadHoldingRegistersAsync((byte)7, 10, 1), Times.Once);
        _mockService.Verify(s => s.WriteSingleRegisterAsync((byte)7, 20, (ushort)7), Times.Once);
    }

    [Fact]
    public async Task Evaluate_ConditionNotMet_DoesNotAct()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 41 });
        using var service = CreateService();
        service.AddRule(MakeRule());

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_ReadReturnsNull_DoesNotAct()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((ushort[]?)null);
        using var service = CreateService();
        service.AddRule(MakeRule());

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_InvalidTriggerValue_DoesNotAct()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        service.AddRule(Vary(r => r.TriggerValue = "not-a-number"));

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_OneTimeRule_TriggersOnlyOnce()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        var rule = Vary(r => r.OneTime = true);
        service.AddRule(rule);

        await service.EvaluateRulesAsync();
        await service.EvaluateRulesAsync();

        Assert.True(rule.Triggered);
        _mockService.Verify(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Once);
    }

    [Fact]
    public void ResetOneTimeRules_AllowsRuleToTriggerAgain()
    {
        using var service = CreateService();
        var rule = Vary(r =>
        {
            r.OneTime = true;
            r.Triggered = true;
        });
        service.AddRule(rule);

        service.ResetOneTimeRules();

        Assert.False(rule.Triggered);
    }

    [Fact]
    public async Task Evaluate_DisabledRule_IsIgnored()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        service.AddRule(Vary(r => r.Enabled = false));

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_GreaterThanOperator_ComparesModuleValues()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 43 });
        using var service = CreateService();
        service.AddRule(Vary(r =>
        {
            r.TriggerOperator = "GreaterThan";
            r.TriggerValue = "42";
        }));

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Once);
    }

    [Fact]
    public async Task Evaluate_NotEqualsOperator_TriggersOnMismatch()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 43 });
        using var service = CreateService();
        service.AddRule(Vary(r =>
        {
            r.TriggerOperator = "NotEquals";
            r.TriggerValue = "42";
        }));

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Once);
    }

    [Fact]
    public async Task Evaluate_CoilArea_ReadsCoilsAndLogs()
    {
        _mockService.Setup(s => s.ReadCoilsAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new bool[] { true });
        using var service = CreateService();
        var rule = Vary(r =>
        {
            r.TriggerArea = "Coil";
            r.TriggerValue = "true";
            r.ActionType = "LogMessage";
            r.LogMessage = "coil saw it";
        });
        service.AddRule(rule);

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.ReadCoilsAsync(It.IsAny<byte>(), 10, 1), Times.Once);
        _mockConsole.Verify(c => c.Log("Rule 'Rule': coil saw it"), Times.Once);
    }

    [Fact]
    public async Task Evaluate_SetCoilAction_WritesCoilWithParsedValue()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        var rule = Vary(r =>
        {
            r.ActionType = "SetCoil";
            r.ActionValue = "1";
        });
        service.AddRule(rule);

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleCoilAsync(It.IsAny<byte>(), 20, true), Times.Once);
    }

    [Fact]
    public async Task Evaluate_LogMessageAction_LogsRuleNameAndMessage()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        var rule = Vary(r =>
        {
            r.ActionType = "LogMessage";
            r.LogMessage = "hello from the rule";
        });
        service.AddRule(rule);

        await service.EvaluateRulesAsync();

        _mockConsole.Verify(c => c.Log("Rule 'Rule': hello from the rule"), Times.Once);
        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        _mockService.Verify(s => s.WriteSingleCoilAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_UnknownActionType_DoesNotWrite()
    {
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ushort[] { 42 });
        using var service = CreateService();
        service.AddRule(Vary(r => r.ActionType = "Bogus"));

        await service.EvaluateRulesAsync();

        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        _mockService.Verify(s => s.WriteSingleCoilAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_OverlappingPasses_DoNotRunConcurrently()
    {
        // A slow Modbus read outlives the 250 ms tick; a second pass started
        // while the first is running must be skipped, not interleaved.
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readResult = gate.Task.ContinueWith(
            _ => (ushort[]?)new ushort[] { 42 },
            TaskScheduler.Default);
        int readStarts = 0;
        _mockService.Setup(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref readStarts);
                return readResult;
            });
        using var service = CreateService();
        service.AddRule(MakeRule());

        // The first call runs synchronously up to its first await (the read),
        // so the guard flag is set before the second call starts.
        var first = service.EvaluateRulesAsync();
        var second = service.EvaluateRulesAsync();

        await second; // the skipped pass completes immediately
        Assert.Equal(1, readStarts); // no interleaved read

        gate.SetResult(true);
        await first; // the first pass now completes

        // The skipped pass means exactly one action write happened.
        _mockService.Verify(s => s.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Once);
    }

    [Fact]
    public async Task GetRegisterValue_UnknownArea_ReturnsNull()
    {
        using var service = CreateService();

        var value = await service.GetRegisterValueAsync("nonexistent", 1);

        Assert.Null(value);
    }

    [Fact]
    public async Task GetRegisterValue_NoActiveService_ReturnsNull()
    {
        _mockConnectionManager.SetupGet(c => c.ActiveService).Returns((IModbusService?)null);
        using var service = CreateService();

        var value = await service.GetRegisterValueAsync("HoldingRegister", 1);

        Assert.Null(value);
        _mockService.Verify(s => s.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void AddRemoveUpdateAndClear_ManageTheRuleCollection()
    {
        using var service = CreateService();
        var rule = MakeRule();
        service.AddRule(rule);
        Assert.Single(service.Rules);

        var replacement = Vary(r => r.ActionValue = "9");
        service.UpdateRule(replacement);
        Assert.Single(service.Rules);
        Assert.Same(replacement, service.Rules[0]);

        service.RemoveRule(replacement);
        Assert.Empty(service.Rules);

        service.AddRule(rule);
        service.ClearRules();
        Assert.Empty(service.Rules);
    }

    [Fact]
    public void UpdateRule_UnknownName_LeavesCollectionUnchanged()
    {
        using var service = CreateService();
        service.AddRule(MakeRule());

        service.UpdateRule(Vary(r => r.Name = "Other"));

        Assert.Single(service.Rules);
    }
}

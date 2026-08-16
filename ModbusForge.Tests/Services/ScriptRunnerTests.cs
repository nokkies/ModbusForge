using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services;

public class ScriptRunnerTests
{
    private readonly Mock<ILogger<ScriptRunner>> _mockLogger;
    private readonly Mock<IModbusService> _mockModbusService;
    private readonly ScriptRunner _runner;
    private readonly byte _unitId = 1;

    public ScriptRunnerTests()
    {
        _mockLogger = new Mock<ILogger<ScriptRunner>>();
        _mockModbusService = new Mock<IModbusService>();
        _runner = new ScriptRunner(_mockLogger.Object);
    }

    [Fact]
    public async Task RunScriptAsync_EmptyScript_CompletesSuccessfully()
    {
        // Arrange
        var script = new Script("Test Script") { Commands = { } };
        bool completedFired = false;
        bool allSuccess = false;
        _runner.ScriptCompleted += (s, success) =>
        {
            completedFired = true;
            allSuccess = success;
        };

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        Assert.True(completedFired);
        Assert.True(allSuccess);
        Assert.False(_runner.IsRunning);
    }

    [Fact]
    public async Task RunScriptAsync_ReadHoldingRegisters_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadHoldingRegisters,
            Address = 10,
            Count = 2
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        _mockModbusService
            .Setup(m => m.ReadHoldingRegistersAsync(_unitId, 10, 2))
            .ReturnsAsync(new ushort[] { 100, 200 });

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadHoldingRegistersAsync(_unitId, 10, 2), Times.Once);
        Assert.True(cmd.LastSuccess);
        Assert.Equal("100, 200", cmd.LastResult);
    }

    [Fact]
    public async Task RunScriptAsync_ReadInputRegisters_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadInputRegisters,
            Address = 20,
            Count = 3
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        _mockModbusService
            .Setup(m => m.ReadInputRegistersAsync(_unitId, 20, 3))
            .ReturnsAsync(new ushort[] { 1, 2, 3 });

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadInputRegistersAsync(_unitId, 20, 3), Times.Once);
        Assert.True(cmd.LastSuccess);
        Assert.Equal("1, 2, 3", cmd.LastResult);
    }

    [Fact]
    public async Task RunScriptAsync_ReadCoils_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadCoils,
            Address = 30,
            Count = 2
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        _mockModbusService
            .Setup(m => m.ReadCoilsAsync(_unitId, 30, 2))
            .ReturnsAsync(new bool[] { true, false });

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadCoilsAsync(_unitId, 30, 2), Times.Once);
        Assert.True(cmd.LastSuccess);
        Assert.Equal("ON, OFF", cmd.LastResult);
    }

    [Fact]
    public async Task RunScriptAsync_ReadDiscreteInputs_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadDiscreteInputs,
            Address = 40,
            Count = 1
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        _mockModbusService
            .Setup(m => m.ReadDiscreteInputsAsync(_unitId, 40, 1))
            .ReturnsAsync(new bool[] { false });

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadDiscreteInputsAsync(_unitId, 40, 1), Times.Once);
        Assert.True(cmd.LastSuccess);
        Assert.Equal("OFF", cmd.LastResult);
    }

    [Fact]
    public async Task RunScriptAsync_WriteSingleRegister_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.WriteSingleRegister,
            Address = 50,
            Value = 1234
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        _mockModbusService
            .Setup(m => m.WriteSingleRegisterAsync(_unitId, 50, 1234))
            .Returns(Task.CompletedTask);

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.WriteSingleRegisterAsync(_unitId, 50, 1234), Times.Once);
        Assert.True(cmd.LastSuccess);
        Assert.Contains("1234", cmd.LastResult);
    }

    [Fact]
    public async Task RunScriptAsync_WriteSingleCoil_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.WriteSingleCoil,
            Address = 60,
            BoolValue = true
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        _mockModbusService
            .Setup(m => m.WriteSingleCoilAsync(_unitId, 60, true))
            .Returns(Task.CompletedTask);

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.WriteSingleCoilAsync(_unitId, 60, true), Times.Once);
        Assert.True(cmd.LastSuccess);
        Assert.Contains("ON", cmd.LastResult);
    }

    [Fact]
    public async Task RunScriptAsync_Delay_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.Delay,
            DelayMs = 50
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        // Act
        var startTime = DateTime.UtcNow;
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);
        var duration = DateTime.UtcNow - startTime;

        // Assert
        Assert.True(cmd.LastSuccess);
        Assert.Contains("50", cmd.LastResult);
        Assert.True(duration.TotalMilliseconds >= 25);
    }

    [Fact]
    public async Task RunScriptAsync_Log_Success()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.Log,
            Message = "Test log message"
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        bool foundMessage = false;
        _runner.LogMessage += (s, msg) =>
        {
            if (msg.Contains("Test log message"))
            {
                foundMessage = true;
            }
        };

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        Assert.True(cmd.LastSuccess);
        Assert.Equal("Test log message", cmd.LastResult);
        Assert.True(foundMessage, "The expected log message was not raised via the LogMessage event.");
    }

    [Fact]
    public async Task RunScriptAsync_DisabledCommand_SkipsCommand()
    {
        // Arrange
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadHoldingRegisters,
            Address = 1,
            Count = 1,
            IsEnabled = false
        };
        var script = new Script("Test") { Commands = { cmd }, DelayBetweenCommandsMs = 0 };

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RunScriptAsync_ModbusError_StopsOnError()
    {
        // Arrange
        var cmd1 = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadHoldingRegisters,
            Address = 1
        };
        var cmd2 = new ScriptCommand
        {
            CommandType = ScriptCommandType.WriteSingleRegister,
            Address = 2
        };
        var script = new Script("Test")
        {
            Commands = { cmd1, cmd2 },
            StopOnError = true,
            DelayBetweenCommandsMs = 0
        };

        _mockModbusService
            .Setup(m => m.ReadHoldingRegistersAsync(_unitId, 1, 1))
            .ThrowsAsync(new Exception("Modbus timeout"));

        bool? scriptSuccess = null;
        _runner.ScriptCompleted += (s, success) => scriptSuccess = success;

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadHoldingRegistersAsync(_unitId, 1, 1), Times.Once);
        _mockModbusService.Verify(m => m.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        Assert.False(cmd1.LastSuccess);
        Assert.Contains("Modbus timeout", cmd1.LastResult);
        Assert.False(scriptSuccess);
    }

    [Fact]
    public async Task RunScriptAsync_ModbusError_ContinuesIfStopOnErrorFalse()
    {
        // Arrange
        var cmd1 = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadHoldingRegisters,
            Address = 1
        };
        var cmd2 = new ScriptCommand
        {
            CommandType = ScriptCommandType.WriteSingleRegister,
            Address = 2,
            Value = 5
        };
        var script = new Script("Test")
        {
            Commands = { cmd1, cmd2 },
            StopOnError = false,
            DelayBetweenCommandsMs = 0
        };

        _mockModbusService
            .Setup(m => m.ReadHoldingRegistersAsync(_unitId, 1, 1))
            .ThrowsAsync(new Exception("Modbus timeout"));

        _mockModbusService
            .Setup(m => m.WriteSingleRegisterAsync(_unitId, 2, 5))
            .Returns(Task.CompletedTask);

        bool? scriptSuccess = null;
        _runner.ScriptCompleted += (s, success) => scriptSuccess = success;

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        _mockModbusService.Verify(m => m.ReadHoldingRegistersAsync(_unitId, 1, 1), Times.Once);
        _mockModbusService.Verify(m => m.WriteSingleRegisterAsync(_unitId, 2, 5), Times.Once);
        Assert.False(cmd1.LastSuccess);
        Assert.True(cmd2.LastSuccess);
        Assert.False(scriptSuccess);
    }

    [Fact]
    public async Task Stop_MidRun_RaisesCancelled_NotFailed()
    {
        // Arrange
        var cmd1 = new ScriptCommand
        {
            CommandType = ScriptCommandType.Delay,
            DelayMs = 5000
        };
        var cmd2 = new ScriptCommand
        {
            CommandType = ScriptCommandType.WriteSingleRegister,
            Address = 1
        };
        var script = new Script("Test")
        {
            Commands = { cmd1, cmd2 },
            DelayBetweenCommandsMs = 0
        };

        bool? completedWith = null;
        bool cancelledRaised = false;
        _runner.ScriptCompleted += (s, success) => completedWith = success;
        _runner.ScriptCancelled += (s, _) => cancelledRaised = true;

        // Act
        var runTask = _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        await Task.Delay(50);
        _runner.Stop();

        await runTask;

        // Assert: a user stop is reported as a cancellation, NOT as a failed
        // completion - the two must be distinguishable in the UI.
        _mockModbusService.Verify(m => m.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        Assert.True(cancelledRaised);
        Assert.True(completedWith == null, "a cancelled script must not also raise ScriptCompleted");
        Assert.False(_runner.IsRunning);
    }

    [Fact]
    public async Task RunScriptAsync_NullReadResult_IsAFailedCommand()
    {
        // Arrange: the device answers nothing - the service reports that as null.
        var read = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadHoldingRegisters,
            Address = 1,
            Count = 2
        };
        var write = new ScriptCommand
        {
            CommandType = ScriptCommandType.WriteSingleRegister,
            Address = 2,
            Value = 5
        };
        var script = new Script("Test")
        {
            Commands = { read, write },
            StopOnError = true,
            DelayBetweenCommandsMs = 0
        };

        _mockModbusService
            .Setup(m => m.ReadHoldingRegistersAsync(_unitId, 1, 2))
            .ReturnsAsync((ushort[]?)null);

        bool? scriptSuccess = null;
        _runner.ScriptCompleted += (s, success) => scriptSuccess = success;

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert: the no-response read failed, and StopOnError halted the script
        // before the write.
        Assert.False(read.LastSuccess);
        Assert.Equal("no response", read.LastResult);
        _mockModbusService.Verify(m => m.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()), Times.Never);
        Assert.False(scriptSuccess);
    }

    [Fact]
    public async Task RunScriptAsync_WhenBusy_SecondStartIsIgnored()
    {
        // Arrange: the first script is long-running.
        var first = new Script("First")
        {
            Commands = { new ScriptCommand { CommandType = ScriptCommandType.Delay, DelayMs = 500 } },
            DelayBetweenCommandsMs = 0
        };
        var second = new Script("Second")
        {
            Commands = { new ScriptCommand
                {
                    CommandType = ScriptCommandType.WriteSingleRegister,
                    Address = 9,
                    Value = 1
                } },
            DelayBetweenCommandsMs = 0
        };

        // Act
        var runTask = _runner.RunScriptAsync(first, _mockModbusService.Object, _unitId);
        await Task.Delay(50);
        Assert.True(_runner.IsRunning);

        var secondTask = _runner.RunScriptAsync(second, _mockModbusService.Object, _unitId);
        await secondTask; // returns immediately - it was ignored

        await runTask;

        // Assert: the ignored start must not have executed any of its commands.
        _mockModbusService.Verify(
            m => m.WriteSingleRegisterAsync(_unitId, 9, 1),
            Times.Never,
            "the rejected second run must not execute its commands");
        Assert.False(_runner.IsRunning);
    }

    [Fact]
    public async Task RunScriptAsync_RepeatCountZero_RunsOnce()
    {
        // Arrange: a file-loaded script could carry RepeatCount 0.
        var cmd = new ScriptCommand
        {
            CommandType = ScriptCommandType.Log,
            Message = "ran"
        };
        var script = new Script("Test")
        {
            Commands = { cmd },
            RepeatCount = 0,
            DelayBetweenCommandsMs = 0
        };

        // Act
        await _runner.RunScriptAsync(script, _mockModbusService.Object, _unitId);

        // Assert
        Assert.True(cmd.LastSuccess, "RepeatCount 0 must clamp to a single pass, not silently skip the script");
    }
}

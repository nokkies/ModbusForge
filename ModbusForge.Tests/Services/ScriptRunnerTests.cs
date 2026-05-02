using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class ScriptRunnerTests
    {
        private readonly Mock<ILogger<ScriptRunner>> _mockLogger;
        private readonly Mock<IModbusService> _mockModbusService;
        private readonly ScriptRunner _scriptRunner;

        public ScriptRunnerTests()
        {
            _mockLogger = new Mock<ILogger<ScriptRunner>>();
            _mockModbusService = new Mock<IModbusService>();
            _scriptRunner = new ScriptRunner(_mockLogger.Object);
        }

        [Fact]
        public async Task RunScriptAsync_HappyPath_ExecutesAllCommands()
        {
            // Arrange
            var script = new Script("Test Script") { RepeatCount = 1 };
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Test 1" });
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Test 2" });

            var startedRaised = false;
            var completedRaised = false;
            bool? successResult = null;
            var executedCommands = new List<ScriptCommand>();

            _scriptRunner.ScriptStarted += (s, e) => startedRaised = true;
            _scriptRunner.ScriptCompleted += (s, success) => { completedRaised = true; successResult = success; };
            _scriptRunner.CommandExecuted += (s, e) => executedCommands.Add(e.Command);

            // Act
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            // Assert
            Assert.True(startedRaised);
            Assert.True(completedRaised);
            Assert.True(successResult);
            Assert.Equal(2, executedCommands.Count);
            Assert.Equal("Test 1", executedCommands[0].Message);
            Assert.Equal("Test 2", executedCommands[1].Message);
        }

        [Fact]
        public async Task RunScriptAsync_DisabledCommand_IsSkipped()
        {
            // Arrange
            var script = new Script("Test Script");
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Enabled", IsEnabled = true });
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Disabled", IsEnabled = false });

            var executedCommands = new List<ScriptCommand>();
            _scriptRunner.CommandExecuted += (s, e) => executedCommands.Add(e.Command);

            // Act
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            // Assert
            Assert.Single(executedCommands);
            Assert.Equal("Enabled", executedCommands[0].Message);
        }

        [Fact]
        public async Task RunScriptAsync_StopOnErrorTrue_StopsExecution()
        {
            // Arrange
            var script = new Script("Test Script") { StopOnError = true };
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.WriteSingleRegister, Address = 1, Value = 100 });
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Should not run" });

            _mockModbusService.Setup(m => m.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()))
                .ThrowsAsync(new Exception("Modbus Error"));

            var executedCommands = new List<ScriptCommand>();
            _scriptRunner.CommandExecuted += (s, e) => executedCommands.Add(e.Command);
            bool? scriptSuccess = null;
            _scriptRunner.ScriptCompleted += (s, success) => scriptSuccess = success;

            // Act
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            // Assert
            Assert.Single(executedCommands);
            Assert.False(executedCommands[0].LastSuccess);
            Assert.False(scriptSuccess);
        }

        [Fact]
        public async Task RunScriptAsync_StopOnErrorFalse_ContinuesExecution()
        {
            // Arrange
            var script = new Script("Test Script") { StopOnError = false };
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.WriteSingleRegister, Address = 1, Value = 100 });
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Should run" });

            _mockModbusService.Setup(m => m.WriteSingleRegisterAsync(It.IsAny<byte>(), It.IsAny<int>(), It.IsAny<ushort>()))
                .ThrowsAsync(new Exception("Modbus Error"));

            var executedCommands = new List<ScriptCommand>();
            _scriptRunner.CommandExecuted += (s, e) => executedCommands.Add(e.Command);
            bool? scriptSuccess = null;
            _scriptRunner.ScriptCompleted += (s, success) => scriptSuccess = success;

            // Act
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            // Assert
            Assert.Equal(2, executedCommands.Count);
            Assert.False(executedCommands[0].LastSuccess);
            Assert.True(executedCommands[1].LastSuccess);
            Assert.False(scriptSuccess);
        }

        [Fact]
        public async Task RunScriptAsync_CancellationRequested_StopsExecution()
        {
            // Arrange
            var script = new Script("Test Script");
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Delay, DelayMs = 5000 });
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Should not run" });

            var cts = new CancellationTokenSource();

            // Act
            var runTask = _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1, cts.Token);
            cts.Cancel();
            await runTask;

            // Assert
            Assert.False(_scriptRunner.IsRunning);
        }

        [Fact]
        public async Task Stop_CancelsRunningScript()
        {
            // Arrange
            var script = new Script("Test Script");
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Delay, DelayMs = 5000 });

            // Act
            var runTask = _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);
            _scriptRunner.Stop();
            await runTask;

            // Assert
            Assert.False(_scriptRunner.IsRunning);
        }

        [Fact]
        public async Task RunScriptAsync_AlreadyRunning_ReturnsEarly()
        {
            // Arrange
            var script = new Script("Test Script");
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Delay, DelayMs = 1000 });

            var logMessages = new List<string>();
            _scriptRunner.LogMessage += (s, msg) => logMessages.Add(msg);

            // Act
            var runTask1 = _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            _scriptRunner.Stop();
            await runTask1;

            // Assert
            Assert.Contains(logMessages, m => m.Contains("Script is already running"));
        }

        [Fact]
        public async Task RunScriptAsync_ModbusReadCommands_UpdateResult()
        {
            // Arrange
            var script = new Script("Test Script");
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.ReadHoldingRegisters, Address = 10, Count = 2 });

            _mockModbusService.Setup(m => m.ReadHoldingRegistersAsync(1, 10, 2))
                .ReturnsAsync(new ushort[] { 123, 456 });

            // Act
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            // Assert
            var cmd = script.Commands[0];
            Assert.True(cmd.LastSuccess);
            Assert.Equal("123, 456", cmd.LastResult);
        }

        [Fact]
        public async Task RunScriptAsync_RepeatCount_ExecutesMultipleTimes()
        {
            // Arrange
            var script = new Script("Test Script") { RepeatCount = 3 };
            script.Commands.Add(new ScriptCommand { CommandType = ScriptCommandType.Log, Message = "Iter" });

            var executionArgs = new List<ScriptExecutionEventArgs>();
            _scriptRunner.CommandExecuted += (s, e) => executionArgs.Add(e);

            // Act
            await _scriptRunner.RunScriptAsync(script, _mockModbusService.Object, 1);

            // Assert
            Assert.Equal(3, executionArgs.Count);
            Assert.Equal(1, executionArgs[0].CurrentRepeat);
            Assert.Equal(2, executionArgs[1].CurrentRepeat);
            Assert.Equal(3, executionArgs[2].CurrentRepeat);
        }
    }
}

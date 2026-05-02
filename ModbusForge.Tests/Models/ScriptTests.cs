using ModbusForge.Models;
using Xunit;

namespace ModbusForge.Tests.Models;

public class ScriptTests
{
    [Fact]
    public void Clone_ShouldCopyPrimitiveProperties()
    {
        // Arrange
        var original = new Script("Test Script")
        {
            Description = "Test Description",
            StopOnError = false,
            RepeatCount = 5,
            DelayBetweenCommandsMs = 500
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.StopOnError, clone.StopOnError);
        Assert.Equal(original.RepeatCount, clone.RepeatCount);
        Assert.Equal(original.DelayBetweenCommandsMs, clone.DelayBetweenCommandsMs);
    }

    [Fact]
    public void Clone_ShouldAppendCopySuffixToName()
    {
        // Arrange
        var original = new Script("Test Script");

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Equal("Test Script (Copy)", clone.Name);
    }

    [Fact]
    public void Clone_ShouldGenerateNewId()
    {
        // Arrange
        var original = new Script("Test Script");

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotEqual(original.Id, clone.Id);
        Assert.False(string.IsNullOrEmpty(clone.Id));
    }

    [Fact]
    public void Clone_ShouldPerformDeepCopyOfCommands()
    {
        // Arrange
        var original = new Script("Test Script");
        var command = new ScriptCommand
        {
            CommandType = ScriptCommandType.ReadHoldingRegisters,
            Address = 100,
            Count = 10
        };
        original.Commands.Add(command);

        // Act
        var clone = original.Clone();

        // Assert
        Assert.Single(clone.Commands);
        Assert.NotSame(original.Commands[0], clone.Commands[0]);
        Assert.Equal(original.Commands[0].CommandType, clone.Commands[0].CommandType);
        Assert.Equal(original.Commands[0].Address, clone.Commands[0].Address);
        Assert.Equal(original.Commands[0].Count, clone.Commands[0].Count);
    }

    [Fact]
    public void Clone_ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new Script("Original");
        original.Commands.Add(new ScriptCommand { Address = 1 });
        var clone = original.Clone();

        // Act
        clone.Name = "Modified Clone";
        clone.Commands[0].Address = 2;
        clone.Commands.Add(new ScriptCommand { Address = 3 });

        // Assert
        Assert.Equal("Original", original.Name);
        Assert.Equal(1, original.Commands[0].Address);
        Assert.Single(original.Commands);
    }

    [Fact]
    public void Clone_ShouldCreateNewCommandsListInstance()
    {
        // Arrange
        var original = new Script("Original");

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original.Commands, clone.Commands);
    }
}

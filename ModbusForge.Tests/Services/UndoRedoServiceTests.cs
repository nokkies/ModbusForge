using System;
using Moq;
using Xunit;
using ModbusForge.Services;
using ModbusForge.Services.EditorCommands;

namespace ModbusForge.Tests.Services
{
    public class UndoRedoServiceTests
    {
        private class MockCommand : IEditorCommand
        {
            public int ExecuteCount { get; private set; }
            public int UnexecuteCount { get; private set; }

            public void Execute()
            {
                ExecuteCount++;
            }

            public void Unexecute()
            {
                UnexecuteCount++;
            }
        }

        [Fact]
        public void Push_AddsToUndoStack()
        {
            var service = new UndoRedoService();
            var command = new MockCommand();

            service.Push(command);

            Assert.True(service.CanUndo);
            Assert.False(service.CanRedo);
        }

        [Fact]
        public void Undo_CallsUnexecuteAndMovesToRedoStack()
        {
            var service = new UndoRedoService();
            var command = new MockCommand();

            service.Push(command);
            service.Undo();

            Assert.Equal(1, command.UnexecuteCount);
            Assert.False(service.CanUndo);
            Assert.True(service.CanRedo);
        }

        [Fact]
        public void Redo_CallsExecuteAndMovesToUndoStack()
        {
            var service = new UndoRedoService();
            var command = new MockCommand();

            service.Push(command);
            service.Undo();
            service.Redo();

            Assert.Equal(1, command.ExecuteCount);
            Assert.True(service.CanUndo);
            Assert.False(service.CanRedo);
        }

        [Fact]
        public void Push_ClearsRedoStack()
        {
            var service = new UndoRedoService();
            var command1 = new MockCommand();
            var command2 = new MockCommand();

            service.Push(command1);
            service.Undo();

            Assert.True(service.CanRedo);

            service.Push(command2);

            Assert.False(service.CanRedo);
        }

        [Fact]
        public void Stack_CappedAt100()
        {
            var service = new UndoRedoService();

            for (int i = 0; i < 105; i++)
            {
                service.Push(new MockCommand());
            }

            // Undo 100 times should be possible
            for (int i = 0; i < 100; i++)
            {
                Assert.True(service.CanUndo);
                service.Undo();
            }

            // The 101st undo should not be possible
            Assert.False(service.CanUndo);
        }

        [Fact]
        public void Undo_WhenEmpty_DoesNothing()
        {
            var service = new UndoRedoService();

            service.Undo(); // Should not throw

            Assert.False(service.CanUndo);
        }

        [Fact]
        public void Redo_WhenEmpty_DoesNothing()
        {
            var service = new UndoRedoService();

            service.Redo(); // Should not throw

            Assert.False(service.CanRedo);
        }

        [Fact]
        public void Clear_EmptiesBothStacks()
        {
            var service = new UndoRedoService();
            var command = new MockCommand();

            service.Push(command);
            service.Undo();

            service.Clear();

            Assert.False(service.CanUndo);
            Assert.False(service.CanRedo);
        }
    }
}

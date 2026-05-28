using System.Collections.Generic;
using ModbusForge.Services.EditorCommands;

namespace ModbusForge.Services
{
    public class UndoRedoService
    {
        private const int MaxHistory = 100;
        private readonly LinkedList<IEditorCommand> _undoStack = new LinkedList<IEditorCommand>();
        private readonly LinkedList<IEditorCommand> _redoStack = new LinkedList<IEditorCommand>();

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Push(IEditorCommand command)
        {
            _undoStack.AddLast(command);
            if (_undoStack.Count > MaxHistory)
            {
                _undoStack.RemoveFirst();
            }
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (CanUndo)
            {
                var command = _undoStack.Last!.Value;
                _undoStack.RemoveLast();
                command.Unexecute();
                _redoStack.AddLast(command);
            }
        }

        public void Redo()
        {
            if (CanRedo)
            {
                var command = _redoStack.Last!.Value;
                _redoStack.RemoveLast();
                command.Execute();
                _undoStack.AddLast(command);
            }
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}

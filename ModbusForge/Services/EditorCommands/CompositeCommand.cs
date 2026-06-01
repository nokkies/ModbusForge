using System.Collections.Generic;

namespace ModbusForge.Services.EditorCommands
{
    public class CompositeCommand : IEditorCommand
    {
        private readonly List<IEditorCommand> _commands;

        public CompositeCommand(List<IEditorCommand> commands)
        {
            _commands = commands;
        }

        public void Execute()
        {
            foreach (var command in _commands)
            {
                command.Execute();
            }
        }

        public void Unexecute()
        {
            // Unexecute in reverse order
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Unexecute();
            }
        }
    }
}

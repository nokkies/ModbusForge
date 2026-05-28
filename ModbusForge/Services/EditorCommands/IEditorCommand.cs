namespace ModbusForge.Services.EditorCommands
{
    public interface IEditorCommand
    {
        void Execute();
        void Unexecute();
    }
}

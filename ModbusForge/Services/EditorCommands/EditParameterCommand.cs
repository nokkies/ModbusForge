using System;
using System.Reflection;

namespace ModbusForge.Services.EditorCommands
{
    public class EditParameterCommand : IEditorCommand
    {
        private readonly object _target;
        private readonly PropertyInfo _propertyInfo;
        private readonly object? _oldValue;
        private readonly object? _newValue;

        public EditParameterCommand(object target, string propertyName, object? oldValue, object? newValue)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _propertyInfo = _target.GetType().GetProperty(propertyName)
                            ?? throw new ArgumentException($"Property '{propertyName}' not found on type {_target.GetType().Name}");
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Execute()
        {
            _propertyInfo.SetValue(_target, _newValue);
        }

        public void Unexecute()
        {
            _propertyInfo.SetValue(_target, _oldValue);
        }
    }
}

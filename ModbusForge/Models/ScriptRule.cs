using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModbusForge.Models
{
    /// <summary>
    /// Represents a script rule that can automate Modbus operations based on conditions
    /// </summary>
    public class ScriptRule : INotifyPropertyChanged
    {
        private string _name = "";
        private bool _enabled = true;
        private string _conditionType = "RegisterValue"; // RegisterValue, CoilsState, TimeBased
        private int _triggerAddress = 1;
        private string _triggerArea = "HoldingRegister"; // HoldingRegister, Coil, InputRegister, DiscreteInput
        private string _triggerOperator = "Equals"; // Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual
        private string _triggerValue = "0";
        private string _actionType = "SetRegister"; // SetRegister, SetCoil, Delay, LogMessage
        private int _actionAddress = 1;
        private string _actionArea = "HoldingRegister";
        private string _actionValue = "1";
        private int _delayMs = 1000;
        private string _logMessage = "Rule triggered";
        private bool _oneTime = false;
        private bool _triggered = false;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public string ConditionType
        {
            get => _conditionType;
            set => SetProperty(ref _conditionType, value);
        }

        public int TriggerAddress
        {
            get => _triggerAddress;
            set => SetProperty(ref _triggerAddress, value);
        }

        public string TriggerArea
        {
            get => _triggerArea;
            set => SetProperty(ref _triggerArea, value);
        }

        public string TriggerOperator
        {
            get => _triggerOperator;
            set => SetProperty(ref _triggerOperator, value);
        }

        public string TriggerValue
        {
            get => _triggerValue;
            set => SetProperty(ref _triggerValue, value);
        }

        public string ActionType
        {
            get => _actionType;
            set => SetProperty(ref _actionType, value);
        }

        public int ActionAddress
        {
            get => _actionAddress;
            set => SetProperty(ref _actionAddress, value);
        }

        public string ActionArea
        {
            get => _actionArea;
            set => SetProperty(ref _actionArea, value);
        }

        public string ActionValue
        {
            get => _actionValue;
            set => SetProperty(ref _actionValue, value);
        }

        public int DelayMs
        {
            get => _delayMs;
            set => SetProperty(ref _delayMs, value);
        }

        public string LogMessage
        {
            get => _logMessage;
            set => SetProperty(ref _logMessage, value);
        }

        public bool OneTime
        {
            get => _oneTime;
            set => SetProperty(ref _oneTime, value);
        }

        public bool Triggered
        {
            get => _triggered;
            set => SetProperty(ref _triggered, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Creates a copy of this rule
        /// </summary>
        public ScriptRule Clone()
        {
            return new ScriptRule
            {
                Name = Name,
                Enabled = Enabled,
                ConditionType = ConditionType,
                TriggerAddress = TriggerAddress,
                TriggerArea = TriggerArea,
                TriggerOperator = TriggerOperator,
                TriggerValue = TriggerValue,
                ActionType = ActionType,
                ActionAddress = ActionAddress,
                ActionArea = ActionArea,
                ActionValue = ActionValue,
                DelayMs = DelayMs,
                LogMessage = LogMessage,
                OneTime = OneTime,
                Triggered = Triggered
            };
        }

        /// <summary>
        /// Returns a human-readable description of the rule
        /// </summary>
        public string GetDescription()
        {
            return $"IF {TriggerArea}[{TriggerAddress}] {TriggerOperator} {TriggerValue} THEN {ActionType} {ActionArea}[{ActionAddress}] = {ActionValue}";
        }
    }
}

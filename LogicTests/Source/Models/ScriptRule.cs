using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Represents a script rule that can automate Modbus operations based on conditions
    /// </summary>
    public partial class ScriptRule : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private bool _enabled = true;

        [ObservableProperty]
        private string _conditionType = "RegisterValue"; // RegisterValue, CoilsState, TimeBased

        [ObservableProperty]
        private int _triggerAddress = 1;

        [ObservableProperty]
        private string _triggerArea = "HoldingRegister"; // HoldingRegister, Coil, InputRegister, DiscreteInput

        [ObservableProperty]
        private string _triggerOperator = "Equals"; // Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual

        [ObservableProperty]
        private string _triggerValue = "0";

        [ObservableProperty]
        private string _actionType = "SetRegister"; // SetRegister, SetCoil, Delay, LogMessage

        [ObservableProperty]
        private int _actionAddress = 1;

        [ObservableProperty]
        private string _actionArea = "HoldingRegister";

        [ObservableProperty]
        private string _actionValue = "1";

        [ObservableProperty]
        private int _delayMs = 1000;

        [ObservableProperty]
        private string _logMessage = "Rule triggered";

        [ObservableProperty]
        private bool _oneTime = false;

        [ObservableProperty]
        private bool _triggered = false;

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

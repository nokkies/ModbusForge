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
        /// Local time of the last time this rule's condition was met and its
        /// action was executed. Null until the rule has triggered at least
        /// once. Set by the rule service; observable so views can refresh.
        /// </summary>
        [ObservableProperty]
        private DateTime? _lastTriggeredAt;

        /// <summary>
        /// Creates a copy of this rule. Runtime state (<see cref="Triggered"/>
        /// and <see cref="LastTriggeredAt"/>) is intentionally not copied.
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
                OneTime = OneTime
            };
        }

        /// <summary>
        /// Human-readable description of the rule, recomputed from the fields
        /// below. Observable: every field that contributes to it re-raises
        /// <see cref="PropertyChanged"/> for this property, so grids and panels
        /// refresh as the user edits the rule.
        /// </summary>
        public string Description =>
            $"IF {TriggerArea}[{TriggerAddress}] {TriggerOperator} {TriggerValue} THEN {ActionType} {ActionArea}[{ActionAddress}] = {ActionValue}";

        /// <summary>
        /// Returns a human-readable description of the rule.
        /// </summary>
        public string GetDescription() => Description;

        partial void OnTriggerAreaChanged(string value) => OnDescriptionChanged();
        partial void OnTriggerAddressChanged(int value) => OnDescriptionChanged();
        partial void OnTriggerOperatorChanged(string value) => OnDescriptionChanged();
        partial void OnTriggerValueChanged(string value) => OnDescriptionChanged();
        partial void OnActionTypeChanged(string value) => OnDescriptionChanged();
        partial void OnActionAreaChanged(string value) => OnDescriptionChanged();
        partial void OnActionAddressChanged(int value) => OnDescriptionChanged();
        partial void OnActionValueChanged(string value) => OnDescriptionChanged();

        private void OnDescriptionChanged()
        {
            OnPropertyChanged(nameof(Description));
        }
    }
}

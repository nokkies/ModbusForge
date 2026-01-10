using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ModbusForge.ViewModels
{
    public partial class ScriptEditorViewModel : ObservableObject
    {
        private readonly IScriptRuleService _scriptRuleService;
        private readonly ILogger<ScriptEditorViewModel> _logger;
        private int _ruleCounter = 1;

        public ObservableCollection<ScriptRule> Rules { get; } = new();

        [ObservableProperty]
        private ScriptRule? _selectedRule;

        [ObservableProperty]
        private bool _rulesEnabled = true;

        public ScriptEditorViewModel(IScriptRuleService scriptRuleService, ILogger<ScriptEditorViewModel> logger)
        {
            _scriptRuleService = scriptRuleService ?? throw new ArgumentNullException(nameof(scriptRuleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Load existing rules
            LoadRules();

            // Subscribe to rule changes
            _scriptRuleService.Rules.CollectionChanged += (s, e) => LoadRules();
        }

        private void LoadRules()
        {
            Rules.Clear();
            foreach (var rule in _scriptRuleService.Rules)
            {
                Rules.Add(rule);
            }
            
            if (Rules.Any() && SelectedRule == null)
            {
                SelectedRule = Rules.First();
            }
        }

        partial void OnSelectedRuleChanged(ScriptRule? value)
        {
            // Update description when rule changes
            OnPropertyChanged(nameof(Description));
        }

        public string Description
        {
            get => SelectedRule?.GetDescription() ?? "Select a rule to edit";
        }

        [RelayCommand]
        private void NewRule()
        {
            var newRule = new ScriptRule
            {
                Name = $"Rule {_ruleCounter++}",
                Enabled = true,
                ConditionType = "RegisterValue",
                TriggerArea = "HoldingRegister",
                TriggerAddress = 1,
                TriggerOperator = "Equals",
                TriggerValue = "0",
                ActionType = "SetRegister",
                ActionArea = "HoldingRegister",
                ActionAddress = 2,
                ActionValue = "1",
                DelayMs = 0,
                OneTime = false
            };

            _scriptRuleService.AddRule(newRule);
            SelectedRule = newRule;
        }

        [RelayCommand]
        private void DeleteRule()
        {
            if (SelectedRule == null) return;

            var result = MessageBox.Show(
                $"Delete rule '{SelectedRule.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _scriptRuleService.RemoveRule(SelectedRule);
                if (!Rules.Any())
                {
                    SelectedRule = null;
                }
            }
        }

        [RelayCommand]
        private void ResetOneTime()
        {
            _scriptRuleService.ResetOneTimeRules();
            MessageBox.Show("One-time rules have been reset.", "Rules Reset", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void ClearAll()
        {
            var result = MessageBox.Show(
                "Delete ALL script rules? This cannot be undone.",
                "Confirm Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _scriptRuleService.ClearRules();
                SelectedRule = null;
            }
        }

        [RelayCommand]
        private void ShowHelp()
        {
            var helpText = @"SCRIPT RULES HELP

OVERVIEW:
Script rules allow you to automate Modbus operations based on conditions. 
Rules are evaluated continuously when enabled.

RULE STRUCTURE:
IF condition THEN action

CONDITIONS:
• RegisterValue: Monitor a register/coil value
• Areas: HoldingRegister, InputRegister, Coil, DiscreteInput
• Operators: Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual
• Values: Numbers (123.45) or booleans (true/false, 1/0)

ACTIONS:
• SetRegister: Write a value to a holding register
• SetCoil: Set a coil state (true/false or 1/0)
• LogMessage: Write a message to the console

EXAMPLES:

1. Basic Automation:
   IF HoldingRegister[1] Equals 100 THEN SetRegister HoldingRegister[2] = 200
   
2. Coil Control:
   IF Coil[5] Equals true THEN SetCoil Coil[6] = false
   
3. Threshold Monitoring:
   IF HoldingRegister[10] GreaterThan 500 THEN SetRegister HoldingRegister[15] = 1
   
4. One-Time Trigger:
   IF HoldingRegister[20] Equals 1 THEN SetRegister HoldingRegister[21] = 999 (One-Time)

OPTIONS:
• Enabled: Turn rule on/off
• One-Time: Rule triggers only once, then must be reset
• Delay (ms): Wait time before executing action

TIPS:
• Use one-time rules for initialization or reset conditions
• Add delays to prevent rapid-fire actions
• Monitor the console for rule execution messages
• Test rules with small values first

For more help, check the ModbusForge documentation.";

            MessageBox.Show(helpText, "Script Rules Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        partial void OnRulesEnabledChanged(bool value)
        {
            _logger.LogInformation("Script rules {0}", value ? "enabled" : "disabled");
        }
    }
}

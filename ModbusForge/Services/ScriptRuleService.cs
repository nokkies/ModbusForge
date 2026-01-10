using ModbusForge.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Service for managing and executing script rules
    /// </summary>
    public class ScriptRuleService : IScriptRuleService, IDisposable
    {
        private readonly ILogger<ScriptRuleService> _logger;
        private readonly IModbusService _modbusService;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly IOptions<ServerSettings> _serverSettings;
        private readonly Timer _evaluationTimer;

        public ObservableCollection<ScriptRule> Rules { get; } = new();

        public ScriptRuleService(
            ILogger<ScriptRuleService> logger,
            IModbusService modbusService,
            IConsoleLoggerService consoleLoggerService,
            IOptions<ServerSettings> serverSettings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));
            _serverSettings = serverSettings ?? throw new ArgumentNullException(nameof(serverSettings));
            
            // Initialize evaluation timer (runs every 250ms)
            _evaluationTimer = new Timer(EvaluateRulesCallback, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        }

        public void AddRule(ScriptRule rule)
        {
            if (rule == null) return;
            
            Rules.Add(rule);
            _logger.LogInformation("Added script rule: {RuleName}", rule.Name);
            _consoleLoggerService.Log($"Script rule added: {rule.Name}");
        }

        public void RemoveRule(ScriptRule rule)
        {
            if (rule == null) return;
            
            if (Rules.Remove(rule))
            {
                _logger.LogInformation("Removed script rule: {RuleName}", rule.Name);
                _consoleLoggerService.Log($"Script rule removed: {rule.Name}");
            }
        }

        public void UpdateRule(ScriptRule rule)
        {
            if (rule == null) return;
            
            var existingRule = Rules.FirstOrDefault(r => r.Name == rule.Name);
            if (existingRule != null)
            {
                int index = Rules.IndexOf(existingRule);
                Rules[index] = rule;
                _logger.LogInformation("Updated script rule: {RuleName}", rule.Name);
                _consoleLoggerService.Log($"Script rule updated: {rule.Name}");
            }
        }

        public async Task EvaluateRulesAsync()
        {
            if (!_modbusService.IsConnected) return;

            foreach (var rule in Rules.Where(r => r.Enabled && !r.Triggered))
            {
                try
                {
                    bool conditionMet = await EvaluateConditionAsync(rule);
                    if (conditionMet)
                    {
                        await ExecuteActionAsync(rule);
                        
                        if (rule.OneTime)
                        {
                            rule.Triggered = true;
                        }
                        
                        _logger.LogInformation("Script rule triggered: {RuleName}", rule.Name);
                        _consoleLoggerService.Log($"Rule triggered: {rule.GetDescription()}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error evaluating script rule: {RuleName}", rule.Name);
                    _consoleLoggerService.Log($"Rule error: {rule.Name} - {ex.Message}");
                }
            }
        }

        public void ResetOneTimeRules()
        {
            foreach (var rule in Rules.Where(r => r.OneTime && r.Triggered))
            {
                rule.Triggered = false;
            }
            _logger.LogInformation("Reset one-time script rules");
            _consoleLoggerService.Log("One-time rules reset");
        }

        public void ClearRules()
        {
            Rules.Clear();
            _logger.LogInformation("Cleared all script rules");
            _consoleLoggerService.Log("All script rules cleared");
        }

        private async void EvaluateRulesCallback(object? state)
        {
            if (!_modbusService.IsConnected) return;
            
            try
            {
                await EvaluateRulesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating script rules in timer callback");
            }
        }

        public void Dispose()
        {
            _evaluationTimer?.Dispose();
        }

        public async Task<object?> GetRegisterValueAsync(string area, int address)
        {
            try
            {
                switch (area.ToLowerInvariant())
                {
                    case "holdingregister":
                        var hr = await _modbusService.ReadHoldingRegistersAsync(_serverSettings.Value.DefaultUnitId, address, 1);
                        return hr?.FirstOrDefault();
                    
                    case "inputregister":
                        var ir = await _modbusService.ReadInputRegistersAsync(_serverSettings.Value.DefaultUnitId, address, 1);
                        return ir?.FirstOrDefault();
                    
                    case "coil":
                        var coils = await _modbusService.ReadCoilsAsync(_serverSettings.Value.DefaultUnitId, address, 1);
                        return coils?.FirstOrDefault();
                    
                    case "discreteinput":
                        var di = await _modbusService.ReadDiscreteInputsAsync(_serverSettings.Value.DefaultUnitId, address, 1);
                        return di?.FirstOrDefault();
                    
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading register value for rule evaluation: {Area}[{Address}]", area, address);
                return null;
            }
        }

        private async Task<bool> EvaluateConditionAsync(ScriptRule rule)
        {
            var currentValue = await GetRegisterValueAsync(rule.TriggerArea, rule.TriggerAddress);
            if (currentValue == null) return false;

            // Convert trigger value to appropriate type
            bool triggerValueParsed = false;
            object? triggerValueObj = null;

            // Try to parse as number first
            if (double.TryParse(rule.TriggerValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double triggerNum))
            {
                triggerValueObj = triggerNum;
                triggerValueParsed = true;
            }
            // Try to parse as boolean
            else if (bool.TryParse(rule.TriggerValue, out bool triggerBool))
            {
                triggerValueObj = triggerBool;
                triggerValueParsed = true;
            }

            if (!triggerValueParsed)
            {
                return false;
            }

            // Compare based on operator
            return rule.TriggerOperator switch
            {
                "Equals" => CompareValues(currentValue, triggerValueObj, (a, b) => a.Equals(b)),
                "NotEquals" => CompareValues(currentValue, triggerValueObj, (a, b) => !a.Equals(b)),
                "GreaterThan" => CompareNumericValues(currentValue, triggerValueObj, (a, b) => a > b),
                "LessThan" => CompareNumericValues(currentValue, triggerValueObj, (a, b) => a < b),
                "GreaterThanOrEqual" => CompareNumericValues(currentValue, triggerValueObj, (a, b) => a >= b),
                "LessThanOrEqual" => CompareNumericValues(currentValue, triggerValueObj, (a, b) => a <= b),
                _ => false
            };
        }

        private bool CompareValues(object currentValue, object triggerValue, Func<object, object, bool> comparison)
        {
            try
            {
                return comparison(currentValue, triggerValue);
            }
            catch
            {
                return false;
            }
        }

        private bool CompareNumericValues(object currentValue, object triggerValue, Func<double, double, bool> comparison)
        {
            try
            {
                double currentNum = Convert.ToDouble(currentValue, CultureInfo.InvariantCulture);
                double triggerNum = Convert.ToDouble(triggerValue, CultureInfo.InvariantCulture);
                return comparison(currentNum, triggerNum);
            }
            catch
            {
                return false;
            }
        }

        private async Task ExecuteActionAsync(ScriptRule rule)
        {
            // Apply delay if specified
            if (rule.DelayMs > 0)
            {
                await Task.Delay(rule.DelayMs);
            }

            switch (rule.ActionType)
            {
                case "SetRegister":
                    await SetRegisterAsync(rule.ActionArea, rule.ActionAddress, rule.ActionValue);
                    break;
                
                case "SetCoil":
                    await SetCoilAsync(rule.ActionAddress, rule.ActionValue);
                    break;
                
                case "LogMessage":
                    _consoleLoggerService.Log($"Rule '{rule.Name}': {rule.LogMessage}");
                    break;
                
                default:
                    _logger.LogWarning("Unknown action type: {ActionType}", rule.ActionType);
                    break;
            }
        }

        private async Task SetRegisterAsync(string area, int address, string value)
        {
            try
            {
                if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort ushortValue))
                {
                    await _modbusService.WriteSingleRegisterAsync(_serverSettings.Value.DefaultUnitId, address, ushortValue);
                    _logger.LogInformation("Rule set register {Area}[{Address}] = {Value}", area, address, value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule failed to set register {Area}[{Address}] = {Value}", area, address, value);
            }
        }

        private async Task SetCoilAsync(int address, string value)
        {
            try
            {
                if (bool.TryParse(value, out bool boolValue))
                {
                    await _modbusService.WriteSingleCoilAsync(_serverSettings.Value.DefaultUnitId, address, boolValue);
                    _logger.LogInformation("Rule set coil[{Address}] = {Value}", address, boolValue);
                }
                else if (int.TryParse(value, out int intValue) && (intValue == 0 || intValue == 1))
                {
                    bool coilState = intValue == 1;
                    await _modbusService.WriteSingleCoilAsync(_serverSettings.Value.DefaultUnitId, address, coilState);
                    _logger.LogInformation("Rule set coil[{Address}] = {Value}", address, coilState);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule failed to set coil[{Address}] = {Value}", address, value);
            }
        }
    }
}

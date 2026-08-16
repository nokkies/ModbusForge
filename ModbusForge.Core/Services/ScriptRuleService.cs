using ModbusForge.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Service for managing and executing script rules
    /// </summary>
    public class ScriptRuleService : IScriptRuleService, IDisposable
    {
        private readonly ILogger<ScriptRuleService> _logger;
        private readonly IConnectionManager _connectionManager;
        private readonly IConsoleLoggerService _consoleLoggerService;
        private readonly Timer _evaluationTimer;

        // Guards against overlapping evaluation passes: a Modbus read can take
        // longer than the 250 ms tick interval, and a second pass running
        // concurrently would re-trigger rules and double-fire actions.
        private int _evaluationInFlight;

        public ObservableCollection<ScriptRule> Rules { get; } = new();

        /// <summary>
        /// Default interval between rule evaluation passes.
        /// </summary>
        public static TimeSpan DefaultEvaluationInterval { get; } = TimeSpan.FromMilliseconds(250);

        public ScriptRuleService(
            ILogger<ScriptRuleService> logger,
            IConnectionManager connectionManager,
            IConsoleLoggerService consoleLoggerService,
            TimeSpan? evaluationInterval = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _consoleLoggerService = consoleLoggerService ?? throw new ArgumentNullException(nameof(consoleLoggerService));

            var interval = evaluationInterval ?? DefaultEvaluationInterval;
            _evaluationTimer = new Timer(EvaluateRulesCallback, null, interval, interval);
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
            // Skip this pass when a previous one is still running (a slow
            // Modbus read can outlast the 250 ms tick); the next tick retries.
            if (Interlocked.Exchange(ref _evaluationInFlight, 1) == 1) return;

            try
            {
                var (service, unitId) = ResolveActiveTarget();
                if (service == null) return;

                foreach (var rule in Rules.Where(r => r.Enabled && !r.Triggered))
                {
                    try
                    {
                        bool conditionMet = await EvaluateConditionAsync(service, unitId, rule);
                        if (conditionMet)
                        {
                            // Record when the rule fired (before the action's
                            // delay/write completes) so views can show it.
                            rule.LastTriggeredAt = DateTime.Now;
                            await ExecuteActionAsync(service, unitId, rule);

                            if (rule.OneTime)
                            {
                                rule.Triggered = true;
                            }

                            _logger.LogInformation("Script rule triggered: {RuleName}", rule.Name);
                            _consoleLoggerService.Log($"Rule triggered: {rule.GetDescription()}");
                        }
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                    {
                        _logger.LogError(ex, "Error evaluating script rule: {RuleName}", rule.Name);
                        _consoleLoggerService.Log($"Rule error: {rule.Name} - {ex.Message}");
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _evaluationInFlight, 0);
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
            _consoleLoggerService.Log("All rules cleared");
        }

        private async void EvaluateRulesCallback(object? state)
        {
            try
            {
                await EvaluateRulesAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Script rule evaluation was canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
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
            var (service, unitId) = ResolveActiveTarget();
            if (service == null) return null;

            try
            {
                switch (area.ToLowerInvariant())
                {
                    case "holdingregister":
                        var hr = await service.ReadHoldingRegistersAsync(unitId, address, 1);
                        return hr?.FirstOrDefault();

                    case "inputregister":
                        var ir = await service.ReadInputRegistersAsync(unitId, address, 1);
                        return ir?.FirstOrDefault();

                    case "coil":
                        var coils = await service.ReadCoilsAsync(unitId, address, 1);
                        return coils?.FirstOrDefault();

                    case "discreteinput":
                        var di = await service.ReadDiscreteInputsAsync(unitId, address, 1);
                        return di?.FirstOrDefault();

                    default:
                        return null;
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Error reading register value for rule evaluation: {Area}[{Address}]", area, address);
                return null;
            }
        }

        /// <summary>
        /// Resolves the Modbus service and unit id that rules should act on:
        /// the currently active connection profile (the same target the
        /// script editor uses). Returns a null service when nothing is
        /// connected, in which case rule evaluation is skipped entirely.
        /// </summary>
        private (IModbusService? Service, byte UnitId) ResolveActiveTarget()
        {
            var service = _connectionManager.ActiveService;
            if (service == null || !service.IsConnected)
            {
                return (null, 1);
            }

            var unitId = _connectionManager.ActiveProfile?.UnitId ?? 1;
            return (service, unitId);
        }

        private async Task<bool> EvaluateConditionAsync(IModbusService service, byte unitId, ScriptRule rule)
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
                "Equals" => ValuesEqual(currentValue, triggerValueObj),
                "NotEquals" => !ValuesEqual(currentValue, triggerValueObj),
                "GreaterThan" => CompareNumericValues(currentValue, triggerValueObj ?? new object(), (a, b) => a > b),
                "LessThan" => CompareNumericValues(currentValue, triggerValueObj ?? new object(), (a, b) => a < b),
                "GreaterThanOrEqual" => CompareNumericValues(currentValue, triggerValueObj ?? new object(), (a, b) => a >= b),
                "LessThanOrEqual" => CompareNumericValues(currentValue, triggerValueObj ?? new object(), (a, b) => a <= b),
                _ => false
            };
        }

        /// <summary>
        /// Equality for rule conditions. Both sides are compared as numbers
        /// whenever that is possible (register values arrive as ushort while
        /// numeric trigger values are parsed as double, so a plain
        /// Object.Equals would never match); booleans compare by value.
        /// </summary>
        private static bool ValuesEqual(object? a, object? b)
        {
            if (a == null || b == null)
            {
                return ReferenceEquals(a, b);
            }

            if (TryToDouble(a, out var an) && TryToDouble(b, out var bn))
            {
                return an == bn;
            }

            try
            {
                return a.Equals(b);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryToDouble(object value, out double number)
        {
            // Treat booleans as non-numeric so bool-vs-bool compares by value.
            if (value is bool)
            {
                number = 0;
                return false;
            }

            try
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                number = 0;
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
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Format error converting values to numeric comparison. Current: '{CurrentValue}', Trigger: '{TriggerValue}'", currentValue, triggerValue);
                return false;
            }
            catch (InvalidCastException ex)
            {
                _logger.LogWarning(ex, "Invalid cast during numeric comparison. Current type: '{CurrentType}', Trigger type: '{TriggerType}'",
                    currentValue?.GetType().Name, triggerValue?.GetType().Name);
                return false;
            }
            catch (OverflowException ex)
            {
                _logger.LogWarning(ex, "Overflow error during numeric conversion. Current: '{CurrentValue}', Trigger: '{TriggerValue}'", currentValue, triggerValue);
                return false;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Unexpected error during numeric value comparison. Current: '{CurrentValue}', Trigger: '{TriggerValue}'", currentValue, triggerValue);
                return false;
            }
        }

        private async Task ExecuteActionAsync(IModbusService service, byte unitId, ScriptRule rule)
        {
            // Apply delay if specified
            if (rule.DelayMs > 0)
            {
                await Task.Delay(rule.DelayMs);
            }

            switch (rule.ActionType)
            {
                case "SetRegister":
                    await SetRegisterAsync(service, unitId, rule.ActionArea, rule.ActionAddress, rule.ActionValue);
                    break;

                case "SetCoil":
                    await SetCoilAsync(service, unitId, rule.ActionAddress, rule.ActionValue);
                    break;

                case "LogMessage":
                    _consoleLoggerService.Log($"Rule '{rule.Name}': {rule.LogMessage}");
                    break;

                default:
                    _logger.LogWarning("Unknown action type: {ActionType}", rule.ActionType);
                    break;
            }
        }

        private async Task SetRegisterAsync(IModbusService service, byte unitId, string area, int address, string value)
        {
            try
            {
                if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort ushortValue))
                {
                    await service.WriteSingleRegisterAsync(unitId, address, ushortValue);
                    _logger.LogInformation("Rule set register {Area}[{Address}] = {Value}", area, address, value);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Rule failed to set register {Area}[{Address}] = {Value}", area, address, value);
            }
        }

        private async Task SetCoilAsync(IModbusService service, byte unitId, int address, string value)
        {
            try
            {
                if (bool.TryParse(value, out bool boolValue))
                {
                    await service.WriteSingleCoilAsync(unitId, address, boolValue);
                    _logger.LogInformation("Rule set coil[{Address}] = {Value}", address, boolValue);
                }
                else if (int.TryParse(value, out int intValue) && (intValue == 0 || intValue == 1))
                {
                    bool coilState = intValue == 1;
                    await service.WriteSingleCoilAsync(unitId, address, coilState);
                    _logger.LogInformation("Rule set coil[{Address}] = {Value}", address, coilState);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogError(ex, "Rule failed to set coil[{Address}] = {Value}", address, value);
            }
        }
    }
}

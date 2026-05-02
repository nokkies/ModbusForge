using ModbusForge.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Service interface for managing and executing script rules
    /// </summary>
    public interface IScriptRuleService
    {
        /// <summary>
        /// List of all script rules
        /// </summary>
        ObservableCollection<ScriptRule> Rules { get; }

        /// <summary>
        /// Adds a new script rule
        /// </summary>
        void AddRule(ScriptRule rule);

        /// <summary>
        /// Removes a script rule
        /// </summary>
        void RemoveRule(ScriptRule rule);

        /// <summary>
        /// Updates an existing script rule
        /// </summary>
        void UpdateRule(ScriptRule rule);

        /// <summary>
        /// Evaluates all enabled rules and executes actions for triggered conditions
        /// </summary>
        Task EvaluateRulesAsync();

        /// <summary>
        /// Resets all one-time rules that have been triggered
        /// </summary>
        void ResetOneTimeRules();

        /// <summary>
        /// Clears all rules
        /// </summary>
        void ClearRules();

        /// <summary>
        /// Gets the current value of a register/coil for rule evaluation
        /// </summary>
        Task<object?> GetRegisterValueAsync(string area, int address);
    }
}

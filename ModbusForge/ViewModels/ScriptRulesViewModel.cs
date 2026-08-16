using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the Rules tab: manages the script rule collection hosted by
    /// <see cref="IScriptRuleService"/> (the same singleton the REST API writes to,
    /// so rules created through either surface show up here live). Rule fields are
    /// edited in place — the rule objects are observable and the service evaluates
    /// their current values on every pass, so changes apply without an explicit
    /// "save rule" step.
    /// </summary>
    public partial class ScriptRulesViewModel : ObservableObject, IDisposable
    {
        private const string RulesFilter = "Script Rules|*.json|All files|*.*";
        private const string DefaultFileName = "rules.json";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly IScriptRuleService _ruleService;
        private readonly IDispatcher _dispatcher;
        private readonly ILogger<ScriptRulesViewModel> _logger;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IMessageBoxService? _messageBoxService;
        private bool _isDisposed;
        private bool _suppressNameGuard;
        private string _lastSeenName = string.Empty;

        private void NotifyAllCommandsCanExecute()
        {
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            ClearAllCommand.NotifyCanExecuteChanged();
            SaveRulesCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// The live rule collection from the service. Bound directly so rules
        /// created or removed through the REST API appear here without a refresh.
        /// </summary>
        public ObservableCollection<ScriptRule> Rules => _ruleService.Rules;

        public IReadOnlyList<string> TriggerAreas { get; } = new[]
        {
            "HoldingRegister", "InputRegister", "Coil", "DiscreteInput"
        };

        public IReadOnlyList<string> TriggerOperators { get; } = new[]
        {
            "Equals", "NotEquals", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"
        };

        public IReadOnlyList<string> ActionTypes { get; } = new[]
        {
            "SetRegister", "SetCoil", "LogMessage"
        };

        public IReadOnlyList<string> ActionAreas { get; } = new[]
        {
            "HoldingRegister", "InputRegister", "Coil", "DiscreteInput"
        };

        [ObservableProperty]
        private ScriptRule? _selectedRule;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private string _descriptionText = string.Empty;

        [ObservableProperty]
        private bool _hasSelectedRule;

        public RelayCommand AddRuleCommand { get; }

        public RelayCommand RemoveSelectedCommand { get; }

        public RelayCommand ResetOneTimeCommand { get; }

        public AsyncRelayCommand ClearAllCommand { get; }

        public AsyncRelayCommand SaveRulesCommand { get; }

        public AsyncRelayCommand LoadRulesCommand { get; }

        public bool CanRemoveSelected => SelectedRule != null;

        public bool CanClearAll => Rules.Count > 0;

        public ScriptRulesViewModel(
            IScriptRuleService ruleService,
            IDispatcher dispatcher,
            ILogger<ScriptRulesViewModel> logger,
            IFileDialogService? fileDialogService = null,
            IFileSystem? fileSystem = null,
            IMessageBoxService? messageBoxService = null)
        {
            _ruleService = ruleService ?? throw new ArgumentNullException(nameof(ruleService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileDialogService = fileDialogService;
            _fileSystem = fileSystem ?? new FileSystem();
            _messageBoxService = messageBoxService;

            _ruleService.Rules.CollectionChanged += OnRulesCollectionChanged;

            AddRuleCommand = new RelayCommand(AddRule);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => CanRemoveSelected);
            ResetOneTimeCommand = new RelayCommand(ResetOneTimeRules);
            ClearAllCommand = new AsyncRelayCommand(ClearAllAsync, () => CanClearAll);
            SaveRulesCommand = new AsyncRelayCommand(SaveRulesAsync, () => _fileDialogService != null && Rules.Count > 0);
            LoadRulesCommand = new AsyncRelayCommand(LoadRulesAsync, () => _fileDialogService != null);
        }

        private void OnRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            NotifyAllCommandsCanExecute();

            if (SelectedRule == null) return;

            // If the selected rule disappeared (removed here or through the API),
            // fall back to another rule or clear the selection.
            if (!Rules.Contains(SelectedRule))
            {
                SelectedRule = Rules.FirstOrDefault();
            }
        }

        private void AddRule()
        {
            var rule = new ScriptRule
            {
                Name = CreateUniqueName($"Rule {Rules.Count + 1}")
            };
            _ruleService.AddRule(rule);
            SelectedRule = rule;
            StatusText = $"Added rule {rule.Name}";
            _logger.LogInformation("Added script rule from UI: {RuleName}", rule.Name);
        }

        private string CreateUniqueName(string candidate)
        {
            var name = candidate;
            var suffix = 2;
            while (Rules.Any(r => string.Equals(r.Name, name, StringComparison.Ordinal)))
            {
                name = $"{candidate} {suffix++}";
            }
            return name;
        }

        private void RemoveSelected()
        {
            if (SelectedRule == null) return;

            var rule = SelectedRule;
            _ruleService.RemoveRule(rule);
            SelectedRule = Rules.FirstOrDefault();
            StatusText = $"Removed rule {rule.Name}";
        }

        private void ResetOneTimeRules()
        {
            _ruleService.ResetOneTimeRules();
            StatusText = "One-time rules armed again";
        }

        private async Task ClearAllAsync()
        {
            if (Rules.Count == 0) return;

            if (_messageBoxService != null)
            {
                var result = await _messageBoxService.ShowAsync(
                    $"Remove all {Rules.Count} rule(s)?",
                    "Clear Rules",
                    DialogButton.YesNo,
                    DialogIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    StatusText = "Clear canceled";
                    return;
                }
            }

            _ruleService.ClearRules();
            SelectedRule = null;
            StatusText = "All rules removed";
        }

        private async Task SaveRulesAsync()
        {
            if (_fileDialogService == null || Rules.Count == 0) return;

            var path = await _fileDialogService.ShowSaveFileDialogAsync("Save Rules", RulesFilter, DefaultFileName);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var json = JsonSerializer.Serialize(Rules.ToList(), SerializerOptions);
                await _fileSystem.WriteAllTextAsync(path, json);
                StatusText = $"Saved {Rules.Count} rule(s) to {Path.GetFileName(path)}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                StatusText = $"Save failed: {ex.Message}";
                _logger.LogError(ex, "Failed to save script rules to {Path}", path);
            }
        }

        private async Task LoadRulesAsync()
        {
            if (_fileDialogService == null) return;

            var path = await _fileDialogService.ShowOpenFileDialogAsync("Load Rules", RulesFilter);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var json = await _fileSystem.ReadAllTextAsync(path);
                var loaded = JsonSerializer.Deserialize<List<ScriptRule>>(json, SerializerOptions)
                             ?? new List<ScriptRule>();

                if (loaded.Count == 0)
                {
                    StatusText = "No rules found in the selected file";
                    return;
                }

                if (_messageBoxService != null && Rules.Count > 0)
                {
                    var result = await _messageBoxService.ShowAsync(
                        $"Replace the current {Rules.Count} rule(s) with the {loaded.Count} from {Path.GetFileName(path)}?",
                        "Load Rules",
                        DialogButton.YesNo,
                        DialogIcon.Question);
                    if (result != DialogResult.Yes)
                    {
                        StatusText = "Load canceled";
                        return;
                    }
                }

                _ruleService.ClearRules();
                foreach (var rule in loaded)
                {
                    _ruleService.AddRule(rule);
                }
                SelectedRule = Rules.FirstOrDefault();
                StatusText = $"Loaded {loaded.Count} rule(s) from {Path.GetFileName(path)}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                StatusText = $"Load failed: {ex.Message}";
                _logger.LogError(ex, "Failed to load script rules from {Path}", path);
            }
        }

        private ScriptRule? _previousSelectedRule;

        // MVVMTK updates the backing field only after OnSelectedRuleChanging
        // returns, so the property getter still sees the outgoing rule here.
        partial void OnSelectedRuleChanging(ScriptRule? value)
        {
            _previousSelectedRule = SelectedRule;
        }

        partial void OnSelectedRuleChanged(ScriptRule? value)
        {
            NotifyAllCommandsCanExecute();
            HasSelectedRule = value != null;

            if (_previousSelectedRule != null)
            {
                _previousSelectedRule.PropertyChanged -= OnSelectedRulePropertyChanged;
            }

            if (value != null)
            {
                _lastSeenName = value.Name;
                value.PropertyChanged += OnSelectedRulePropertyChanged;
            }

            DescriptionText = value?.GetDescription() ?? string.Empty;
        }

        private void OnSelectedRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var rule = SelectedRule;
            if (rule == null) return;

            if (e.PropertyName == nameof(ScriptRule.Name))
            {
                EnforceUniqueName(rule);
            }

            // Keep the "name before the last change" snapshot up to date for the
            // duplicate-name guard; PropertyChanged fires after the value changed,
            // so the previous event's snapshot is still the old name here.
            _lastSeenName = rule.Name;

            DescriptionText = rule.GetDescription();
        }

        /// <summary>
        /// Two rules with the same name would make the API upsert (which matches by
        /// name) ambiguous, so a rename that collides with an existing rule is
        /// rejected and the previous name is restored.
        /// </summary>
        private void EnforceUniqueName(ScriptRule rule)
        {
            if (_suppressNameGuard) return;

            var hasDuplicate = Rules.Any(r => !ReferenceEquals(r, rule) &&
                                              string.Equals(r.Name, rule.Name, StringComparison.Ordinal));
            if (!hasDuplicate) return;

            _suppressNameGuard = true;
            rule.Name = _lastSeenName;
            _suppressNameGuard = false;
            _lastSeenName = rule.Name;

            StatusText = "A rule with that name already exists";
            _logger.LogWarning("Rejected duplicate rule name {Name}", rule.Name);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _ruleService.Rules.CollectionChanged -= OnRulesCollectionChanged;

            var selected = SelectedRule;
            if (selected != null)
            {
                selected.PropertyChanged -= OnSelectedRulePropertyChanged;
            }
            SelectedRule = null;
        }
    }
}

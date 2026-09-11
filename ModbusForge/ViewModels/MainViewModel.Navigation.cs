using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Helpers;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// MainViewModel - Navigation partial (split for navigability; behavior unchanged).
    /// </summary>
    public partial class MainViewModel
    {

        // TabItem indices in MainTabControl (MainView.axaml). Referenced by the navigation
        // model and the open-tab commands instead of magic numbers.
        private const int DashboardTabIndex = 0;

        private const int TrendsTabIndex = 1;

        private const int FrameInspectorTabIndex = 2;

        private const int MqttTabIndex = 3;

        private const int ScriptEditorTabIndex = 4;

        private const int SignalGeneratorTabIndex = 5;

        private const int SimulationTabIndex = 6;

        private const int HoldingRegistersTabIndex = 7;

        private const int CoilsTabIndex = 8;

        private const int InputRegistersTabIndex = 9;

        private const int DiscreteInputsTabIndex = 10;

        private const int CustomWatchTabIndex = 11;

        private const int DecodeTabIndex = 12;

        private const int ConsoleTabIndex = 13;

        private const int DebugTabIndex = 14;

        public ICommand ToggleThemeCommand { get; }

        public ICommand ShowAllTabsCommand { get; }

        public ICommand ResetTabsCommand { get; }

        public ICommand ClearConsoleCommand { get; }

        public ICommand ClearDebugCommand { get; }


        public bool IsDarkMode
        {
            get => _themeService?.IsDarkMode ?? false;
            set
            {
                if (_themeService != null && _themeService.IsDarkMode != value)
                {
                    _themeService.SetTheme(value);
                }

                OnPropertyChanged();
            }
        }


        [ObservableProperty]
        private int _selectedTabIndex;


        /// <summary>
        /// Left navigation list entries (see MainView.axaml). Only entries whose tab is
        /// currently visible are present - hidden tabs are removed from the list, so the
        /// view does not need per-item visibility bindings.
        /// </summary>
        public ObservableCollection<NavigationItem> NavigationItems { get; } = new();


        private readonly List<NavigationItem> _allNavigationItems = new();


        [ObservableProperty]
        private NavigationItem? _selectedNavigationItem;


        partial void OnSelectedNavigationItemChanged(NavigationItem? value)
        {
            if (value is not null && SelectedTabIndex != value.TabIndex)
            {
                SelectedTabIndex = value.TabIndex;
            }
        }


        [ObservableProperty]
        private bool _isRegistersTabVisible = true;


        [ObservableProperty]
        private bool _isInputRegistersTabVisible = true;


        [ObservableProperty]
        private bool _isCoilsTabVisible = true;


        [ObservableProperty]
        private bool _isDiscreteInputsTabVisible = true;


        [ObservableProperty]
        private bool _isCustomWatchTabVisible = true;


        [ObservableProperty]
        private bool _isSimulationTabVisible = true;


        [ObservableProperty]
        private bool _isDecodeTabVisible = true;


        [ObservableProperty]
        private bool _isTrendTabVisible = true;


        [ObservableProperty]
        private bool _isConsoleTabVisible = true;


        [ObservableProperty]
        private bool _isDebugTabVisible = true;


        /// <summary>
        /// Console tab backing store. When the shared <see cref="IConsoleLoggerService"/> is
        /// available (the normal DI case) this is the same collection the Modbus services,
        /// script engine, and API server already log into - so the tab shows backend
        /// messages too, and the configured MaxConsoleMessages cap applies.
        /// </summary>
        public ObservableCollection<string> ConsoleMessages { get; }


        private readonly ObservableCollection<string> _consoleMessageFallback = new();


        public ObservableCollection<string> DebugMessages { get; } = new();


        public string VersionText => $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";


        partial void OnIsRegistersTabVisibleChanged(bool value) => OnTabVisibilityChanged(HoldingRegistersTabIndex);

        partial void OnIsInputRegistersTabVisibleChanged(bool value) => OnTabVisibilityChanged(InputRegistersTabIndex);

        partial void OnIsCoilsTabVisibleChanged(bool value) => OnTabVisibilityChanged(CoilsTabIndex);

        partial void OnIsDiscreteInputsTabVisibleChanged(bool value) => OnTabVisibilityChanged(DiscreteInputsTabIndex);

        partial void OnIsCustomWatchTabVisibleChanged(bool value) => OnTabVisibilityChanged(CustomWatchTabIndex);

        partial void OnIsSimulationTabVisibleChanged(bool value) => OnTabVisibilityChanged(SimulationTabIndex);

        partial void OnIsDecodeTabVisibleChanged(bool value) => OnTabVisibilityChanged(DecodeTabIndex);

        partial void OnIsTrendTabVisibleChanged(bool value) => OnTabVisibilityChanged(TrendsTabIndex);

        partial void OnIsConsoleTabVisibleChanged(bool value) => OnTabVisibilityChanged(ConsoleTabIndex);

        partial void OnIsDebugTabVisibleChanged(bool value) => OnTabVisibilityChanged(DebugTabIndex);


        private string? _lastConsoleMessage;


        private void AppendConsoleMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // The poll loop re-sets the same status message every cycle; appending every
            // repetition is what used to spam the console panel.
            if (message == _lastConsoleMessage) return;
            _lastConsoleMessage = message;

            if (_consoleLoggerService != null)
            {
                // Shared sink: dispatches to the UI thread and enforces the configured
                // MaxConsoleMessages cap.
                _consoleLoggerService.Log(message);
                return;
            }

            // Fallback (no DI-provided console service, e.g. some test setups).
            var cap = Math.Max(1, _settingsService?.MaxConsoleMessages ?? 1000);
            ConsoleMessages.Add(message);
            while (ConsoleMessages.Count > cap)
            {
                ConsoleMessages.RemoveAt(0);
            }
        }


        private void AppendDebugMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            DebugMessages.Add(message);
            while (DebugMessages.Count > 1000)
            {
                DebugMessages.RemoveAt(0);
            }
        }


        public void ShowAllTabs()
        {
            IsRegistersTabVisible = true;
            IsInputRegistersTabVisible = true;
            IsCoilsTabVisible = true;
            IsDiscreteInputsTabVisible = true;
            IsCustomWatchTabVisible = true;
            IsSimulationTabVisible = true;
            IsDecodeTabVisible = true;
            IsTrendTabVisible = true;
            IsConsoleTabVisible = true;
            IsDebugTabVisible = true;
        }


        public void ResetTabs() => ShowAllTabs();


        public List<string> GetVisibleTabs()
        {
            var visibleTabs = new List<string>();
            if (IsRegistersTabVisible) visibleTabs.Add("Registers");
            if (IsInputRegistersTabVisible) visibleTabs.Add("InputRegisters");
            if (IsCoilsTabVisible) visibleTabs.Add("Coils");
            if (IsDiscreteInputsTabVisible) visibleTabs.Add("DiscreteInputs");
            if (IsCustomWatchTabVisible) visibleTabs.Add("CustomWatch");
            if (IsSimulationTabVisible) visibleTabs.Add("Simulation");
            if (IsDecodeTabVisible) visibleTabs.Add("Decode");
            if (IsTrendTabVisible) visibleTabs.Add("Trend");
            if (IsConsoleTabVisible) visibleTabs.Add("Console");
            if (IsDebugTabVisible) visibleTabs.Add("Debug");
            return visibleTabs;
        }


        public void SetVisibleTabs(IReadOnlyCollection<string>? visibleTabs)
        {
            if (visibleTabs == null || visibleTabs.Count == 0)
            {
                ShowAllTabs();
                return;
            }

            IsRegistersTabVisible = visibleTabs.Contains("Registers");
            IsInputRegistersTabVisible = visibleTabs.Contains("InputRegisters");
            IsCoilsTabVisible = visibleTabs.Contains("Coils");
            IsDiscreteInputsTabVisible = visibleTabs.Contains("DiscreteInputs");
            IsCustomWatchTabVisible = visibleTabs.Contains("CustomWatch");
            IsSimulationTabVisible = visibleTabs.Contains("Simulation");
            IsDecodeTabVisible = visibleTabs.Contains("Decode");
            IsTrendTabVisible = visibleTabs.Contains("Trend");
            IsConsoleTabVisible = visibleTabs.Contains("Console");
            IsDebugTabVisible = visibleTabs.Contains("Debug");
        }


        private void EnsureSelectedTabIsVisible()
        {
            if (IsTabIndexVisible(SelectedTabIndex)) return;

            SelectedTabIndex = Enumerable.Range(0, _allNavigationItems.Count).FirstOrDefault(IsTabIndexVisible);
        }


        private bool IsTabIndexVisible(int index)
        {
            return index switch
            {
                TrendsTabIndex => IsTrendTabVisible,
                SimulationTabIndex => IsSimulationTabVisible,
                HoldingRegistersTabIndex => IsRegistersTabVisible,
                CoilsTabIndex => IsCoilsTabVisible,
                InputRegistersTabIndex => IsInputRegistersTabVisible,
                DiscreteInputsTabIndex => IsDiscreteInputsTabVisible,
                CustomWatchTabIndex => IsCustomWatchTabVisible,
                DecodeTabIndex => IsDecodeTabVisible,
                ConsoleTabIndex => IsConsoleTabVisible,
                DebugTabIndex => IsDebugTabVisible,
                _ => true
            };
        }


        private void ThemeService_ThemeChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(IsDarkMode));
        }


        partial void OnSelectedTabIndexChanged(int value)
        {
            IsRegisterGridEditing = false;

            // Keep the navigation list in sync when the tab is changed programmatically.
            var item = _allNavigationItems.FirstOrDefault(i => i.TabIndex == value && i.IsVisible);
            if (!ReferenceEquals(item, SelectedNavigationItem))
            {
                SelectedNavigationItem = item;
            }
        }


        /// <summary>
        /// Shared handler for the tab-visibility flags: re-anchors the selection if the
        /// current tab was hidden and updates the (filtered) navigation list.
        /// </summary>
        private void OnTabVisibilityChanged(int tabIndex)
        {
            EnsureSelectedTabIsVisible();
            RefreshNavigationItems();

            if (SelectedNavigationItem is { IsVisible: false })
            {
                // The selected entry just disappeared from the list - re-select to match
                // the (re-anchored) tab.
                OnSelectedTabIndexChanged(SelectedTabIndex);
            }
        }


        /// <summary>Rebuilds the visible subset of the navigation list in place.</summary>
        private void RefreshNavigationItems()
        {
            foreach (var item in _allNavigationItems)
            {
                if (item.IsVisible && !NavigationItems.Contains(item))
                {
                    NavigationItems.Add(item);
                }
                else if (!item.IsVisible)
                {
                    NavigationItems.Remove(item);
                }
            }
        }


        private void ToggleTheme()
        {
            _themeService?.ToggleTheme();
        }

    }
}

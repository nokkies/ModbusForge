using System.Windows;
using Microsoft.Extensions.Logging;
using ModbusForge.ViewModels;
using ModbusForge.Views;

namespace ModbusForge.Services
{
    /// <summary>
    /// Creates and shows shell-level dialog windows, receiving all dependencies
    /// through its constructor rather than a service locator.
    /// </summary>
    public class ShellWindowService : IShellWindowService
    {
        private readonly ILogger<AboutWindow> _aboutLogger;
        private readonly IDialogService _dialogService;
        private readonly HelpViewModel _helpViewModel;
        private readonly IScriptRunner _scriptRunner;
        private readonly IModbusService _modbusService;
        private readonly ISettingsService _settingsService;
        private readonly IConnectionManager _connectionManager;

        public ShellWindowService(
            ILogger<AboutWindow> aboutLogger,
            IDialogService dialogService,
            HelpViewModel helpViewModel,
            IScriptRunner scriptRunner,
            IModbusService modbusService,
            ISettingsService settingsService,
            IConnectionManager connectionManager)
        {
            _aboutLogger = aboutLogger ?? throw new ArgumentNullException(nameof(aboutLogger));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _helpViewModel = helpViewModel ?? throw new ArgumentNullException(nameof(helpViewModel));
            _scriptRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        public void ShowAbout(Window owner)
        {
            var about = new AboutWindow(_aboutLogger, _dialogService)
            {
                Owner = owner
            };
            about.ShowDialog();
        }

        public void ShowHelp(Window owner)
        {
            var helpWindow = new HelpWindow(_helpViewModel)
            {
                Owner = owner
            };
            helpWindow.ShowDialog();
        }

        public void ShowKeyboardShortcuts(Window owner)
        {
            var shortcuts = new KeyboardShortcutsWindow(_dialogService)
            {
                Owner = owner
            };
            shortcuts.ShowDialog();
        }

        public void ShowTroubleshooting(Window owner)
        {
            var troubleshootingWindow = new TroubleshootingWindow(_dialogService)
            {
                Owner = owner
            };
            troubleshootingWindow.ShowDialog();
        }

        public void ShowScriptEditor(Window owner, byte unitId)
        {
            var scriptEditor = new ScriptEditorWindow(_scriptRunner, _modbusService, unitId, _dialogService)
            {
                Owner = owner
            };
            scriptEditor.ShowDialog();
        }

        public void ShowPreferences(Window owner)
        {
            var preferencesWindow = new PreferencesWindow(_settingsService, _dialogService)
            {
                Owner = owner
            };
            preferencesWindow.ShowDialog();
        }

        public void ShowConnectionManager(Window owner)
        {
            var connectionManagerWindow = new ConnectionManagerWindow(_connectionManager, _dialogService)
            {
                Owner = owner
            };
            connectionManagerWindow.ShowDialog();
        }
    }
}

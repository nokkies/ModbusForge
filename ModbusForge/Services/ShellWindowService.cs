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
        private readonly IFileDialogService _fileDialogService;
        private readonly IDispatcher _dispatcher;

        public ShellWindowService(
            ILogger<AboutWindow> aboutLogger,
            IDialogService dialogService,
            HelpViewModel helpViewModel,
            IScriptRunner scriptRunner,
            IModbusService modbusService,
            ISettingsService settingsService,
            IConnectionManager connectionManager,
            IFileDialogService fileDialogService,
            IDispatcher dispatcher)
        {
            _aboutLogger = aboutLogger ?? throw new ArgumentNullException(nameof(aboutLogger));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _helpViewModel = helpViewModel ?? throw new ArgumentNullException(nameof(helpViewModel));
            _scriptRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
            var viewModel = new ScriptEditorViewModel(_scriptRunner, _modbusService, unitId, _dialogService, _fileDialogService, _dispatcher);
            var scriptEditor = new ScriptEditorWindow(viewModel)
            {
                Owner = owner
            };
            scriptEditor.ShowDialog();
        }

        public void ShowPreferences(Window owner)
        {
            var viewModel = new PreferencesViewModel(_settingsService, _dialogService);
            var preferencesWindow = new PreferencesWindow(viewModel)
            {
                Owner = owner
            };
            preferencesWindow.ShowDialog();
        }

        public void ShowConnectionManager(Window owner)
        {
            var viewModel = new ConnectionManagerViewModel(_connectionManager, _dialogService);
            var connectionManagerWindow = new ConnectionManagerWindow(viewModel)
            {
                Owner = owner
            };
            connectionManagerWindow.ShowDialog();
        }
    }
}

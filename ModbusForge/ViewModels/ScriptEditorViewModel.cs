using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class ScriptEditorViewModel : ObservableObject
    {
        private readonly IScriptRunner _scriptRunner;
        private readonly IConnectionManager _connectionManager;
        private readonly IDispatcher _dispatcher;
        private readonly IFileDialogService? _fileDialogService;
        private readonly IMessageBoxService? _messageBoxService;

        [ObservableProperty]
        private Script _script;

        [ObservableProperty]
        private ScriptCommand? _selectedCommand;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isAutoScroll = true;

        public ObservableCollection<string> OutputLog { get; } = new();

        public bool CanRun => !IsRunning && Script.Commands.Count > 0;
        public bool CanRemoveSelected => SelectedCommand != null;
        public bool CanCloneSelected => SelectedCommand != null;
        public bool CanMoveUp => SelectedCommand != null && Script.Commands.IndexOf(SelectedCommand) > 0;
        public bool CanMoveDown => SelectedCommand != null && Script.Commands.IndexOf(SelectedCommand) < Script.Commands.Count - 1;

        public ICommand AddCommand { get; }
        public IRelayCommand RemoveCommand { get; }
        public IRelayCommand MoveUpCommand { get; }
        public IRelayCommand MoveDownCommand { get; }
        public IRelayCommand CloneCommand { get; }
        public IRelayCommand ClearLogCommand { get; }
        public IAsyncRelayCommand RunScriptCommand { get; }
        public IRelayCommand StopScriptCommand { get; }
        public IAsyncRelayCommand SaveScriptCommand { get; }
        public IAsyncRelayCommand LoadScriptCommand { get; }

        public ScriptEditorViewModel(
            IScriptRunner scriptRunner,
            IConnectionManager connectionManager,
            IDispatcher dispatcher,
            IFileDialogService? fileDialogService = null,
            IMessageBoxService? messageBoxService = null)
        {
            _scriptRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _fileDialogService = fileDialogService;
            _messageBoxService = messageBoxService;

            _script = new Script("New Script");

            AddCommand = new RelayCommand(AddCommandInternal);
            RemoveCommand = new RelayCommand(RemoveCommandInternal, () => CanRemoveSelected);
            MoveUpCommand = new RelayCommand(MoveUp, () => CanMoveUp);
            MoveDownCommand = new RelayCommand(MoveDown, () => CanMoveDown);
            CloneCommand = new RelayCommand(CloneCommandInternal, () => CanCloneSelected);
            ClearLogCommand = new RelayCommand(() => OutputLog.Clear(), () => OutputLog.Count > 0);
            RunScriptCommand = new AsyncRelayCommand(RunScriptAsync, () => CanRun);
            StopScriptCommand = new RelayCommand(StopScript, () => IsRunning);
            SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
            LoadScriptCommand = new AsyncRelayCommand(LoadScriptAsync);

            _scriptRunner.LogMessage += OnLogMessage;
            _scriptRunner.ScriptStarted += OnScriptStarted;
            _scriptRunner.ScriptCompleted += OnScriptCompleted;
            _scriptRunner.ScriptCancelled += OnScriptCancelled;
            _scriptRunner.CommandExecuted += OnCommandExecuted;
            _script.Commands.CollectionChanged += Commands_CollectionChanged;
            OutputLog.CollectionChanged += (_, _) => ClearLogCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedCommandChanged(ScriptCommand? value)
        {
            RemoveCommand.NotifyCanExecuteChanged();
            CloneCommand.NotifyCanExecuteChanged();
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsRunningChanged(bool value)
        {
            RunScriptCommand.NotifyCanExecuteChanged();
            StopScriptCommand.NotifyCanExecuteChanged();
        }

        private void Commands_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RunScriptCommand.NotifyCanExecuteChanged();
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }

        private void AddCommandInternal()
        {
            var cmd = new ScriptCommand
            {
                CommandType = ScriptCommandType.ReadHoldingRegisters,
                Address = 1,
                Count = 1
            };
            Script.Commands.Add(cmd);
            SelectedCommand = cmd;
        }

        private void RemoveCommandInternal()
        {
            if (SelectedCommand == null) return;
            var index = Script.Commands.IndexOf(SelectedCommand);
            Script.Commands.Remove(SelectedCommand);
            SelectedCommand = Script.Commands.Count > 0
                ? Script.Commands[Math.Max(0, Math.Min(index, Script.Commands.Count - 1))]
                : null;
        }

        private void MoveUp()
        {
            if (SelectedCommand == null) return;
            var index = Script.Commands.IndexOf(SelectedCommand);
            if (index > 0)
            {
                Script.Commands.Move(index, index - 1);
            }
        }

        private void MoveDown()
        {
            if (SelectedCommand == null) return;
            var index = Script.Commands.IndexOf(SelectedCommand);
            if (index < Script.Commands.Count - 1)
            {
                Script.Commands.Move(index, index + 1);
            }
        }

        private void CloneCommandInternal()
        {
            if (SelectedCommand == null) return;
            var clone = SelectedCommand.Clone();
            Script.Commands.Add(clone);
            SelectedCommand = clone;
        }

        private async Task RunScriptAsync()
        {
            var service = _connectionManager.ActiveService;
            if (service == null || !service.IsConnected)
            {
                await (_messageBoxService?.ShowAsync("Please connect to a Modbus device first.", "Not Connected", DialogButton.Ok, DialogIcon.Warning) ?? Task.CompletedTask);
                return;
            }

            if (Script.Commands.Count == 0)
            {
                await (_messageBoxService?.ShowAsync("Please add at least one command to the script.", "Empty Script", DialogButton.Ok, DialogIcon.Warning) ?? Task.CompletedTask);
                return;
            }

            OutputLog.Clear();

            var unitId = (byte)(_connectionManager.ActiveProfile?.UnitId ?? 1);

            // IsRunning is set by the runner's ScriptStarted event, not here: if
            // the runner is busy with another script (e.g. started via the REST
            // API), this call is rejected without raising any events, and a
            // preemptive IsRunning=true would leave the UI stuck on "Running...".
            await _scriptRunner.RunScriptAsync(Script, service, unitId, CancellationToken.None);
        }

        private void StopScript()
        {
            _scriptRunner.Stop();
        }

        private async Task SaveScriptAsync()
        {
            if (_fileDialogService == null) return;

            var path = await _fileDialogService.ShowSaveFileDialogAsync(
                "Save Script",
                "ModbusForge Scripts|*.mbscript|All files|*.*",
                Script.Name + ".mbscript");

            if (path == null) return;

            try
            {
                var json = JsonSerializer.Serialize(Script, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);
                StatusText = $"Saved to {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        private async Task LoadScriptAsync()
        {
            if (_fileDialogService == null) return;

            var path = await _fileDialogService.ShowOpenFileDialogAsync(
                "Load Script",
                "ModbusForge Scripts|*.mbscript;*.json|All files|*.*");

            if (path == null) return;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var loaded = JsonSerializer.Deserialize<Script>(json);
                if (loaded != null)
                {
                    Script = loaded;
                    Script.Commands.CollectionChanged += Commands_CollectionChanged;
                    OnPropertyChanged(nameof(CanRun));
                    RunScriptCommand.NotifyCanExecuteChanged();
                    StatusText = $"Loaded {Path.GetFileName(path)}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private void OnLogMessage(object? sender, string e)
        {
            _dispatcher.Invoke(() =>
            {
                OutputLog.Add(e);
                while (OutputLog.Count > 1000)
                    OutputLog.RemoveAt(0);
                ClearLogCommand.NotifyCanExecuteChanged();
            });
        }

        private void OnScriptStarted(object? sender, EventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                IsRunning = true;
                StatusText = "Running...";
            });
        }

        private void OnScriptCompleted(object? sender, bool e)
        {
            _dispatcher.Invoke(() =>
            {
                IsRunning = false;
                StatusText = $"Script completed: {(e ? "SUCCESS" : "FAILED")}";
            });
        }

        private void OnScriptCancelled(object? sender, EventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                IsRunning = false;
                StatusText = "Script stopped by user";
            });
        }

        private void OnCommandExecuted(object? sender, ScriptExecutionEventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                e.Command.LastSuccess = e.Success;
                e.Command.LastResult = e.Result;
            });
        }
    }
}

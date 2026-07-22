using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    public class ScriptEditorViewModel : ViewModelBase
    {
        private readonly IScriptRunner _scriptRunner;
        private readonly IModbusService? _modbusService;
        private readonly IDialogService _dialogService;
        private readonly IFileDialogService _fileDialogService;
        private readonly IDispatcher _dispatcher;
        private readonly byte _unitId;
        private CancellationTokenSource? _cts;

        private ScriptCommand? _selectedCommand;
        private bool _isRunning;
        private string _statusText = string.Empty;

        public ScriptEditorViewModel(
            IScriptRunner scriptRunner,
            IModbusService? modbusService,
            byte unitId,
            IDialogService? dialogService = null,
            IFileDialogService? fileDialogService = null,
            IDispatcher? dispatcher = null)
        {
            _scriptRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
            _modbusService = modbusService;
            _unitId = unitId;
            _dialogService = dialogService ?? new NullDialogService();
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            Script = new Script("New Script");
            OutputLog = new ObservableCollection<string>();

            AddCommand = new RelayCommand(AddCommandInternal);
            RemoveCommand = new RelayCommand(RemoveCommandInternal, () => CanRemoveSelected);
            MoveUpCommand = new RelayCommand(MoveUp, () => CanMoveUp);
            MoveDownCommand = new RelayCommand(MoveDown, () => CanMoveDown);
            CloneCommand = new RelayCommand(CloneCommandInternal, () => CanCloneSelected);
            RunScriptCommand = new AsyncRelayCommand(RunScriptAsync, () => CanRun);
            StopScriptCommand = new RelayCommand(StopScript, () => IsRunning);
            ClearLogCommand = new RelayCommand(() => OutputLog.Clear(), () => OutputLog.Count > 0);
            SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
            LoadScriptCommand = new AsyncRelayCommand(LoadScriptAsync);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));

            Script.Commands.CollectionChanged += Commands_CollectionChanged;
            OutputLog.CollectionChanged += OutputLog_CollectionChanged;

            _scriptRunner.CommandExecuted += ScriptRunner_CommandExecuted;
            _scriptRunner.LogMessage += ScriptRunner_LogMessage;
            _scriptRunner.ScriptStarted += ScriptRunner_ScriptStarted;
            _scriptRunner.ScriptCompleted += ScriptRunner_ScriptCompleted;
        }

        public Script Script { get; }
        public ObservableCollection<string> OutputLog { get; }

        public ScriptCommand? SelectedCommand
        {
            get => _selectedCommand;
            set
            {
                if (SetProperty(ref _selectedCommand, value))
                {
                    OnPropertyChanged(nameof(CanRemoveSelected));
                    OnPropertyChanged(nameof(CanCloneSelected));
                    OnPropertyChanged(nameof(CanMoveUp));
                    OnPropertyChanged(nameof(CanMoveDown));
                }
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(CanRun));
                    RunScriptCommand.NotifyCanExecuteChanged();
                    StopScriptCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

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
        public IAsyncRelayCommand RunScriptCommand { get; }
        public IRelayCommand StopScriptCommand { get; }
        public IRelayCommand ClearLogCommand { get; }
        public IAsyncRelayCommand SaveScriptCommand { get; }
        public IAsyncRelayCommand LoadScriptCommand { get; }
        public ICommand CloseCommand { get; }

        public event EventHandler? RequestClose;

        private void Commands_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanRun));
            RunScriptCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        private void OutputLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ClearLogCommand.NotifyCanExecuteChanged();
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
            if (_modbusService == null || !_modbusService.IsConnected)
            {
                _dialogService.Show("Please connect to a Modbus device first.", "Not Connected",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (Script.Commands.Count == 0)
            {
                _dialogService.Show("Please add at least one command to the script.", "Empty Script",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            StatusText = "Running...";
            try
            {
                await _scriptRunner.RunScriptAsync(Script, _modbusService, _unitId, _cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _dialogService.Show(ex.Message, "Script Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void StopScript()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            _scriptRunner.Stop();
        }

        private async Task SaveScriptAsync()
        {
            var filePath = _fileDialogService.ShowSaveFileDialog(
                "Save Script",
                "Script files (*.mbscript)|*.mbscript|All files (*.*)|*.*",
                $"{Script.Name}.mbscript");

            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var data = new ScriptData
                {
                    Name = Script.Name,
                    Description = Script.Description,
                    StopOnError = Script.StopOnError,
                    RepeatCount = Script.RepeatCount,
                    DelayBetweenCommandsMs = Script.DelayBetweenCommandsMs,
                    Commands = Script.Commands.Select(c => new CommandData
                    {
                        CommandType = c.CommandType.ToString(),
                        Address = c.Address,
                        Count = c.Count,
                        Value = c.Value,
                        BoolValue = c.BoolValue,
                        DelayMs = c.DelayMs,
                        Message = c.Message,
                        LoopCount = c.LoopCount,
                        IsEnabled = c.IsEnabled
                    }).ToList()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                await using var stream = File.Create(filePath);
                await JsonSerializer.SerializeAsync(stream, data, options);

                _dialogService.Show("Script saved successfully.", "Save Script",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _dialogService.Show($"Failed to save script: {ex.Message}", "Save Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task LoadScriptAsync()
        {
            var filePath = _fileDialogService.ShowOpenFileDialog(
                "Load Script",
                "Script files (*.mbscript)|*.mbscript|All files (*.*)|*.*");

            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSize)
                {
                    _dialogService.Show($"The selected file is too large (max {MaxFileSize / 1024 / 1024}MB).",
                        "Load Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                await using var stream = File.OpenRead(filePath);
                var data = await JsonSerializer.DeserializeAsync<ScriptData>(stream);

                if (data != null)
                {
                    Script.Name = data.Name;
                    Script.Description = data.Description;
                    Script.StopOnError = data.StopOnError;
                    Script.RepeatCount = data.RepeatCount;
                    Script.DelayBetweenCommandsMs = data.DelayBetweenCommandsMs;
                    Script.Commands.Clear();

                    foreach (var cmdData in data.Commands ?? new List<CommandData>())
                    {
                        if (Enum.TryParse<ScriptCommandType>(cmdData.CommandType, out var cmdType))
                        {
                            Script.Commands.Add(new ScriptCommand
                            {
                                CommandType = cmdType,
                                Address = cmdData.Address,
                                Count = cmdData.Count,
                                Value = cmdData.Value,
                                BoolValue = cmdData.BoolValue,
                                DelayMs = cmdData.DelayMs,
                                Message = cmdData.Message,
                                LoopCount = cmdData.LoopCount,
                                IsEnabled = cmdData.IsEnabled
                            });
                        }
                    }

                    OnPropertyChanged(nameof(Script));
                    SelectedCommand = Script.Commands.FirstOrDefault();
                    StatusText = "Script loaded";
                    _dialogService.Show("Script loaded successfully.", "Load Script",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _dialogService.Show($"Failed to load script: {ex.Message}", "Load Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ScriptRunner_CommandExecuted(object? sender, ScriptExecutionEventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                StatusText = $"Command {e.CommandIndex + 1}/{e.TotalCommands} (Repeat {e.CurrentRepeat}/{e.TotalRepeats})";
            });
        }

        private void ScriptRunner_LogMessage(object? sender, string e)
        {
            _dispatcher.Invoke(() =>
            {
                OutputLog.Add(e);
            });
        }

        private void ScriptRunner_ScriptStarted(object? sender, EventArgs e)
        {
            _dispatcher.Invoke(() =>
            {
                IsRunning = true;
                StatusText = "Running...";
            });
        }

        private void ScriptRunner_ScriptCompleted(object? sender, bool success)
        {
            _dispatcher.Invoke(() =>
            {
                IsRunning = false;
                StatusText = success ? "Completed successfully" : "Completed with errors";
            });
        }

        public override void Dispose()
        {
            Script.Commands.CollectionChanged -= Commands_CollectionChanged;
            OutputLog.CollectionChanged -= OutputLog_CollectionChanged;

            _scriptRunner.CommandExecuted -= ScriptRunner_CommandExecuted;
            _scriptRunner.LogMessage -= ScriptRunner_LogMessage;
            _scriptRunner.ScriptStarted -= ScriptRunner_ScriptStarted;
            _scriptRunner.ScriptCompleted -= ScriptRunner_ScriptCompleted;

            _cts?.Dispose();

            base.Dispose();
        }

        private const long MaxFileSize = 1 * 1024 * 1024; // 1MB limit for scripts

        public class ScriptData
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool StopOnError { get; set; }
            public int RepeatCount { get; set; }
            public int DelayBetweenCommandsMs { get; set; }
            public List<CommandData> Commands { get; set; } = new();
        }

        public class CommandData
        {
            public string CommandType { get; set; } = string.Empty;
            public int Address { get; set; }
            public int Count { get; set; }
            public ushort Value { get; set; }
            public bool BoolValue { get; set; }
            public int DelayMs { get; set; }
            public string Message { get; set; } = string.Empty;
            public int LoopCount { get; set; }
            public bool IsEnabled { get; set; }
        }
    }
}

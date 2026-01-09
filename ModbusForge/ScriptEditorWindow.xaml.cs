using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge;

public partial class ScriptEditorWindow : MetroWindow, INotifyPropertyChanged
{
    private readonly IScriptRunner _scriptRunner;
    private readonly IModbusService? _modbusService;
    private readonly byte _unitId;
    private CancellationTokenSource? _cts;
    private ScriptCommand? _selectedCommand;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Script Script { get; }

    public ScriptCommand? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            _selectedCommand = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCommand)));
        }
    }

    public ScriptEditorWindow(IScriptRunner scriptRunner, IModbusService? modbusService, byte unitId)
    {
        InitializeComponent();
        _scriptRunner = scriptRunner;
        _modbusService = modbusService;
        _unitId = unitId;
        Script = new Script("New Script");
        DataContext = this;

        _scriptRunner.CommandExecuted += ScriptRunner_CommandExecuted;
        _scriptRunner.LogMessage += ScriptRunner_LogMessage;
        _scriptRunner.ScriptStarted += ScriptRunner_ScriptStarted;
        _scriptRunner.ScriptCompleted += ScriptRunner_ScriptCompleted;
    }

    private void ScriptRunner_CommandExecuted(object? sender, ScriptExecutionEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Command {e.CommandIndex + 1}/{e.TotalCommands} (Repeat {e.CurrentRepeat}/{e.TotalRepeats})";
        });
    }

    private void ScriptRunner_LogMessage(object? sender, string e)
    {
        Dispatcher.Invoke(() =>
        {
            OutputLog.Items.Add(e);
            OutputLog.ScrollIntoView(OutputLog.Items[^1]);
        });
    }

    private void ScriptRunner_ScriptStarted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Running...";
        });
    }

    private void ScriptRunner_ScriptCompleted(object? sender, bool success)
    {
        Dispatcher.Invoke(() =>
        {
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = success ? "Completed successfully" : "Completed with errors";
        });
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e)
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

    private void RemoveCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand != null)
        {
            Script.Commands.Remove(SelectedCommand);
            SelectedCommand = Script.Commands.Count > 0 ? Script.Commands[0] : null;
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand == null) return;
        var index = Script.Commands.IndexOf(SelectedCommand);
        if (index > 0)
        {
            Script.Commands.Move(index, index - 1);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand == null) return;
        var index = Script.Commands.IndexOf(SelectedCommand);
        if (index < Script.Commands.Count - 1)
        {
            Script.Commands.Move(index, index + 1);
        }
    }

    private void CloneCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCommand != null)
        {
            var clone = SelectedCommand.Clone();
            Script.Commands.Add(clone);
            SelectedCommand = clone;
        }
    }

    private async void RunScript_Click(object sender, RoutedEventArgs e)
    {
        if (_modbusService == null || !_modbusService.IsConnected)
        {
            MessageBox.Show("Please connect to a Modbus device first.", "Not Connected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Script.Commands.Count == 0)
        {
            MessageBox.Show("Please add at least one command to the script.", "Empty Script",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        await _scriptRunner.RunScriptAsync(Script, _modbusService, _unitId, _cts.Token);
    }

    private void StopScript_Click(object sender, RoutedEventArgs e)
    {
        _scriptRunner.Stop();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        OutputLog.Items.Clear();
    }

    private void SaveScript_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Script files (*.mbscript)|*.mbscript|All files (*.*)|*.*",
            FileName = $"{Script.Name}.mbscript"
        };

        if (dlg.ShowDialog() == true)
        {
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

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);
                MessageBox.Show("Script saved successfully.", "Save Script", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save script: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadScript_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Script files (*.mbscript)|*.mbscript|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var data = JsonSerializer.Deserialize<ScriptData>(json);

                if (data != null)
                {
                    Script.Name = data.Name;
                    Script.Description = data.Description;
                    Script.StopOnError = data.StopOnError;
                    Script.RepeatCount = data.RepeatCount;
                    Script.DelayBetweenCommandsMs = data.DelayBetweenCommandsMs;
                    Script.Commands.Clear();

                    foreach (var cmdData in data.Commands)
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

                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Script)));
                    MessageBox.Show("Script loaded successfully.", "Load Script", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load script: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _scriptRunner.CommandExecuted -= ScriptRunner_CommandExecuted;
        _scriptRunner.LogMessage -= ScriptRunner_LogMessage;
        _scriptRunner.ScriptStarted -= ScriptRunner_ScriptStarted;
        _scriptRunner.ScriptCompleted -= ScriptRunner_ScriptCompleted;
        base.OnClosing(e);
    }

    private class ScriptData
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool StopOnError { get; set; }
        public int RepeatCount { get; set; }
        public int DelayBetweenCommandsMs { get; set; }
        public List<CommandData> Commands { get; set; } = new();
    }

    private class CommandData
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

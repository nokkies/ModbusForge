using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models;

public partial class Script : ObservableObject
{
    [ObservableProperty]
    private string _name = "New Script";

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _stopOnError = true;

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private int _delayBetweenCommandsMs = 100;

    public ObservableCollection<ScriptCommand> Commands { get; } = new();

    public Script() { }

    public Script(string name)
    {
        Name = name;
    }

    public Script Clone()
    {
        var clone = new Script
        {
            Name = Name + " (Copy)",
            Description = Description,
            StopOnError = StopOnError,
            RepeatCount = RepeatCount,
            DelayBetweenCommandsMs = DelayBetweenCommandsMs
        };

        foreach (var cmd in Commands)
        {
            clone.Commands.Add(cmd.Clone());
        }

        return clone;
    }
}

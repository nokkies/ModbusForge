using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models;

public partial class Script : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

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

    // The setter is required for System.Text.Json round-trips: get-only
    // collection properties are silently skipped on deserialization, which
    // previously caused loaded scripts to lose every command.
    public ObservableCollection<ScriptCommand> Commands { get; set; } = new();

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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.Services.Api;

/// <summary>
/// Thin adapter that exposes the subset of <see cref="MainViewModel"/> required by
/// <see cref="IAppStateAccessor"/>, forwarding <see cref="INotifyPropertyChanged"/>
/// events as-is.
/// </summary>
internal sealed class MainViewModelAppStateAccessor : IAppStateAccessor
{
    private readonly MainViewModel _vm;

    public MainViewModelAppStateAccessor(MainViewModel vm)
        => _vm = vm ?? throw new System.ArgumentNullException(nameof(vm));

    public bool IsConnected => _vm.IsConnected;
    public string Mode => _vm.Mode;

    public ICommand ConnectCommand => _vm.ConnectCommand;
    public ICommand DisconnectCommand => _vm.DisconnectCommand;

    public ObservableCollection<CustomEntry> CustomEntries => _vm.CustomEntries;

    public ObservableCollection<VisualNode> SimulationNodes
        => _vm.CurrentConfig.SimulationSettings.VisualNodes;

    // Forward every PropertyChanged notification so that WpfApiApplicationService
    // event subscriptions work correctly without knowing about MainViewModel.
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _vm.PropertyChanged += value;
        remove => _vm.PropertyChanged -= value;
    }
}

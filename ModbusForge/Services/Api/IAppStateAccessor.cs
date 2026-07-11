using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ModbusForge.Models;

namespace ModbusForge.Services.Api;

/// <summary>
/// Narrow accessor to WPF application state that the API needs to read or mutate.
/// This interface is implemented by <see cref="MainViewModelAppStateAccessor"/>, which wraps
/// MainViewModel.  It exists so that <see cref="WpfApiApplicationService"/> and the API
/// endpoints do not take a compile-time dependency on the ViewModel graph.
/// </summary>
public interface IAppStateAccessor : INotifyPropertyChanged
{
    bool IsConnected { get; }
    string Mode { get; }

    ICommand ConnectCommand { get; }
    ICommand DisconnectCommand { get; }

    ObservableCollection<CustomEntry> CustomEntries { get; }
    ObservableCollection<VisualNode> SimulationNodes { get; }
}

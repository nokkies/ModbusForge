using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ModbusForge.Models;

namespace ModbusForge.Services.Api;

/// <summary>
/// Narrow accessor to application state that the API needs to read or mutate.
/// This interface is implemented by <see cref="MainViewModelAppStateAccessor"/>, which wraps
/// MainViewModel.  It exists so that <see cref="ApiApplicationService"/> and the API
/// endpoints do not take a compile-time dependency on the ViewModel graph.
/// </summary>
public interface IAppStateAccessor : INotifyPropertyChanged
{
    bool IsConnected { get; }
    string Mode { get; }

    /// <summary>
    /// True when the most recent connection attempt failed. The view model
    /// clears it at the start of every new attempt, so while a connect is in
    /// flight the flag only ever reflects that attempt.
    /// </summary>
    bool HasConnectionError { get; }

    /// <summary>Last status text (carries the failure reason when <see cref="HasConnectionError"/> is set).</summary>
    string StatusMessage { get; }

    ICommand ConnectCommand { get; }
    ICommand DisconnectCommand { get; }

    ObservableCollection<CustomEntry> CustomEntries { get; }
    ObservableCollection<VisualNode> SimulationNodes { get; }
}

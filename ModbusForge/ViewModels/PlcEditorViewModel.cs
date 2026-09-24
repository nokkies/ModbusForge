using Microsoft.Extensions.Logging;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels;

/// <summary>
/// The PLC tab's editor for FBD logic imported from a Unity Pro XEF: the
/// controller's program, with its own program tree, canvas and simulation engine.
/// It is deliberately not the Simulation tab's editor (the plant model): the two
/// are separate programs that only meet on the Modbus registers, so an import,
/// program selection or Run/Stop here never touches the Simulation.
/// </summary>
public sealed class PlcEditorViewModel : VisualNodeEditorViewModel
{
    /// <remarks>
    /// There is intentionally no <see cref="IVisualSimulationService"/> parameter:
    /// the container would inject the Simulation tab's engine, and one engine runs
    /// one graph, so a PLC Run/Stop would hijack or halt the plant scan. The engine
    /// gets the shared connection manager, so while a connection is active both
    /// engines read and write the same device registers.
    /// </remarks>
    public PlcEditorViewModel(
        ITagWindowService tagWindowService,
        IFileDialogService? fileDialogService = null,
        IMessageBoxService? messageBoxService = null,
        TagService? tagService = null,
        ILogger<VisualNodeEditorViewModel>? logger = null,
        ILogger<AvaloniaVisualSimulationService>? engineLogger = null,
        IConsoleLoggerService? consoleLoggerService = null,
        IConnectionManager? connectionManager = null)
        : base(
            new AvaloniaVisualSimulationService(engineLogger, consoleLoggerService, connectionManager),
            tagWindowService,
            fileDialogService,
            messageBoxService,
            tagService,
            logger)
    {
    }
}

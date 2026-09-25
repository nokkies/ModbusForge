using System.Linq;
using Microsoft.Extensions.Logging;
using ModbusForge.Core.Plc;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels;

/// <summary>
/// The PLC tab's editor for FBD logic imported from a Unity Pro XEF: the
/// controller's program, with its own program tree, canvas and runtime.
/// It is deliberately not the Simulation tab's editor (the plant model): the two
/// are separate programs that only meet on the Modbus registers, so an import,
/// program selection or Run/Stop here never touches the Simulation.
/// </summary>
public sealed class PlcEditorViewModel : VisualNodeEditorViewModel
{
    /// <remarks>
    /// There is intentionally no <see cref="IVisualSimulationService"/> parameter:
    /// the container would inject the Simulation tab's engine. Run here scans the
    /// whole imported project in a <see cref="PlcRuntimeService"/> instead, which gets
    /// the shared connection manager, so while a connection is active the PLC and the
    /// Simulation read and write the same device registers.
    /// </remarks>
    public PlcEditorViewModel(
        ITagWindowService tagWindowService,
        IFileDialogService? fileDialogService = null,
        IMessageBoxService? messageBoxService = null,
        TagService? tagService = null,
        ILogger<VisualNodeEditorViewModel>? logger = null,
        ILogger<PlcRuntimeService>? runtimeLogger = null,
        IConsoleLoggerService? consoleLoggerService = null,
        IConnectionManager? connectionManager = null)
        : base(new PlcRuntimeService(runtimeLogger, consoleLoggerService, connectionManager),
            tagWindowService, fileDialogService, messageBoxService, tagService, logger)
    {
    }

    /// <summary>What Run scans: the imported project in the PLC runtime.</summary>
    /// <remarks>Read through the base class: its constructor can already start a run.</remarks>
    public PlcRuntimeService PlcRuntime => (PlcRuntimeService)SimulationService;

    /// <summary>
    /// Hands the project an XEF import compiled to the runtime; the next Run is a
    /// cold start. Stops a running PLC first.
    /// </summary>
    public void LoadProject(PlcProject? project)
    {
        if (IsRunning) StopCommand.Execute(null);
        PlcRuntime.Load(project);
    }

    protected override string RunningStatusText(string storeMode)
    {
        if (PlcRuntime.Runtime?.Project is not { } project)
        {
            return "No PLC project loaded: import an XEF to run it";
        }

        var sections = project.Sections.Count();
        var blocks = project.Blocks.ToList();
        var computed = blocks.Count(b => b.IsSimulated);
        var text = $"Running {sections} sections: {computed} of {blocks.Count} blocks computed";
        if (computed < blocks.Count)
        {
            text += $", {blocks.Count - computed} keep their outputs (protected DFB code or unsupported type)";
        }
        return text + $", {storeMode}";
    }

    protected override string StoppedStatusText => "Stopped";
}

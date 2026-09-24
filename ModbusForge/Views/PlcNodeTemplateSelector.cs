using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.Views
{
    /// <summary>
    /// Picks the PLC canvas template per node: Control Expert rendering for blocks and
    /// text boxes imported from a XEF, the editor's regular node template for blocks
    /// added by hand.
    /// </summary>
    public sealed class PlcNodeTemplateSelector : IDataTemplate
    {
        public IDataTemplate? PlcBlockTemplate { get; set; }

        public IDataTemplate? CommentTemplate { get; set; }

        public IDataTemplate? DefaultTemplate { get; set; }

        public Control? Build(object? param) => Select(param)?.Build(param);

        public bool Match(object? data) => data is VisualNode;

        private IDataTemplate? Select(object? data) => data switch
        {
            VisualNode { ElementType: PlcElementType.PlcComment, Plc: not null } => CommentTemplate,
            VisualNode { Plc: not null } => PlcBlockTemplate,
            _ => DefaultTemplate
        };
    }
}

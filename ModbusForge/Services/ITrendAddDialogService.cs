namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Outcome of the "Add Trend Pen" dialog.
    /// </summary>
    /// <param name="type">
    /// Value type the pen should read with ("int", "uint", "real", "string"),
    /// known only when the pen comes from a tag. Null → the pen defaults to "int".
    /// </param>
    public sealed record TrendAddDialogResult(string Area, int Address, string Name, int ReadPeriodMs, string? Type = null);

    /// <summary>
    /// Shows the "Add Trend Pen" dialog (register or tag source) and returns
    /// the requested pen, or null when the user cancels. Synchronous API via
    /// a nested dispatcher frame, mirroring <see cref="ICustomBulkAddDialogService"/>.
    /// </summary>
    public interface ITrendAddDialogService
    {
        TrendAddDialogResult? TryGetAddTrendPen();
    }
}

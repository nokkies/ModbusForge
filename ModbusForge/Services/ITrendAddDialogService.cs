namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Outcome of the "Add Trend Pen" dialog.
    /// </summary>
    public sealed record TrendAddDialogResult(string Area, int Address, string Name, int ReadPeriodMs);

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

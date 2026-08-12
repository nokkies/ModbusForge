namespace ModbusForge.Services
{
    /// <summary>
    /// User-supplied values for adding a contiguous range of custom watch entries.
    /// </summary>
    /// <param name="StartRegister">First Modbus address in the range.</param>
    /// <param name="Count">Number of entries to create.</param>
    /// <param name="Area">Modbus area for the entries.</param>
    /// <param name="Type">Data type for the entries.</param>
    /// <param name="ReadPeriodMs">Read/monitor period in milliseconds.</param>
    /// <param name="WritePeriodMs">Continuous-write period in milliseconds.</param>
    /// <param name="NamePrefix">Prefix used to generate entry names.</param>
    public sealed record CustomBulkAddDialogResult(
        int StartRegister,
        int Count,
        string Area,
        string Type,
        int ReadPeriodMs,
        int WritePeriodMs,
        string NamePrefix);

    /// <summary>
    /// Abstracts the bulk-add custom entry dialog to keep view models testable.
    /// </summary>
    public interface ICustomBulkAddDialogService
    {
        /// <summary>
        /// Shows the bulk-add dialog and returns the user supplied values when accepted.
        /// </summary>
        /// <param name="result">The bulk-add options, or <c>null</c> if the user cancels.</param>
        /// <returns><c>true</c> if the user accepted; otherwise <c>false</c>.</returns>
        bool TryGetBulkAdd(out CustomBulkAddDialogResult? result);
    }
}

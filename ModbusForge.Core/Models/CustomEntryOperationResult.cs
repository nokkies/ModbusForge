namespace ModbusForge.Models
{
    /// <summary>
    /// Result of a single custom entry read or write operation.
    /// </summary>
    public class CustomEntryOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

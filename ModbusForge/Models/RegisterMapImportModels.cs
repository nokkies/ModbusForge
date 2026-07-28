using System.Collections.Generic;

namespace ModbusForge.Models
{
    /// <summary>
    /// A single problem found while parsing a register-map/template file.
    /// </summary>
    public class RegisterMapImportIssue
    {
        /// <summary>1-based row number in the source file (including the header row).</summary>
        public int RowNumber { get; set; }

        public string Column { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public override string ToString() =>
            string.IsNullOrEmpty(Column)
                ? $"Row {RowNumber}: {Message}"
                : $"Row {RowNumber} [{Column}]: {Message}";
    }

    /// <summary>
    /// Outcome of parsing a register-map/template file into tags.
    /// </summary>
    public class RegisterMapImportResult
    {
        /// <summary>Tags successfully parsed. Rows with errors are omitted.</summary>
        public List<Tag> Tags { get; } = new();

        /// <summary>Rows that could not be imported.</summary>
        public List<RegisterMapImportIssue> Errors { get; } = new();

        /// <summary>Rows that were imported but had recoverable problems (e.g. defaulted values).</summary>
        public List<RegisterMapImportIssue> Warnings { get; } = new();

        /// <summary>Number of data rows encountered, excluding the header and blank rows.</summary>
        public int RowsRead { get; set; }

        public bool HasErrors => Errors.Count > 0;
    }
}

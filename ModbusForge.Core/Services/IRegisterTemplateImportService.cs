using System.Collections.Generic;
using System.IO;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Parses vendor register maps / device templates (CSV and Excel) into a
    /// <see cref="RegisterTemplate"/> and merges its entries into the tag database.
    /// </summary>
    public interface IRegisterTemplateImportService
    {
        /// <summary>
        /// Parses a register map from disk. The format is chosen from the file extension
        /// (.csv/.txt/.tsv are parsed as delimited text, .xlsx as Excel).
        /// </summary>
        RegisterTemplateImportResult ImportFromFile(string filePath, AddressingConvention addressing = AddressingConvention.ZeroBased);

        /// <summary>Parses a delimited-text (CSV) register map.</summary>
        RegisterTemplateImportResult ImportCsv(TextReader reader, AddressingConvention addressing = AddressingConvention.ZeroBased);

        /// <summary>Parses an Excel (.xlsx) register map, using the first worksheet.</summary>
        RegisterTemplateImportResult ImportExcel(Stream stream, AddressingConvention addressing = AddressingConvention.ZeroBased);

        /// <summary>
        /// Merges template entries into the tag database, creating missing groups and skipping
        /// entries whose tag name already exists. Returns the tags that were actually added.
        /// </summary>
        IReadOnlyList<Tag> Merge(TagService tagService, IEnumerable<RegisterTemplateEntry> entries, out IReadOnlyList<string> skippedNames);

        /// <summary>A CSV template with the supported header row and example rows.</summary>
        string GetCsvTemplate();
    }
}

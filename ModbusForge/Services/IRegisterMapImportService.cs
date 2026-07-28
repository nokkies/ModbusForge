using System.Collections.Generic;
using System.IO;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Parses device templates / register maps (CSV and Excel) into <see cref="Tag"/> definitions.
    /// </summary>
    public interface IRegisterMapImportService
    {
        /// <summary>
        /// Parses a register map from disk. The format is chosen from the file extension
        /// (.csv/.txt/.tsv are parsed as delimited text, .xlsx as Excel).
        /// </summary>
        RegisterMapImportResult ImportFromFile(string filePath);

        /// <summary>Parses a delimited-text (CSV) register map.</summary>
        RegisterMapImportResult ImportCsv(TextReader reader);

        /// <summary>Parses an Excel (.xlsx) register map, using the first worksheet.</summary>
        RegisterMapImportResult ImportExcel(Stream stream);

        /// <summary>
        /// Merges parsed tags into the tag database, creating missing groups and skipping
        /// tags whose name already exists. Returns the tags that were actually added.
        /// </summary>
        IReadOnlyList<Tag> Merge(TagService tagService, IEnumerable<Tag> tags, out IReadOnlyList<string> skippedNames);

        /// <summary>A CSV template with the supported header row and one example line.</summary>
        string GetCsvTemplate();
    }
}

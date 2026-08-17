using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Persists imported register templates as JSON under %AppData%\ModbusForge\templates.
    /// </summary>
    public interface IRegisterTemplateStore
    {
        /// <summary>Directory the templates are stored in.</summary>
        string TemplatesDirectory { get; }

        /// <summary>Writes the template and returns the file it was saved to.</summary>
        string Save(RegisterTemplate template);

        /// <summary>
        /// Loads a template, or null when the file is missing/unreadable or does
        /// not contain a register template.
        /// </summary>
        RegisterTemplate? Load(string filePath);

        IReadOnlyList<string> ListTemplateFiles();
    }

    /// <inheritdoc />
    public class RegisterTemplateStore : IRegisterTemplateStore
    {
        // Windows reserves device names (CON, PRN, NUL, COM1, LPT1, ...) as the
        // file NAME; a template called "con" would sanitize to "CON.json" and the
        // write would throw at runtime. Names are truncated to stay under the
        // 255-char path-component limit, keeping the ".json" extension.
        private const int MaxFileNameLength = 240;

        private static readonly string[] ReservedFileNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM10", "COM11", "COM12", "COM13", "COM14", "COM15", "COM16", "COM17", "COM18", "COM19", "COM20", "COM21", "COM22", "COM23", "COM24", "COM25", "COM26", "COM27", "COM28", "COM29", "COM30", "COM31", "COM32", "COM33", "COM34", "COM35", "COM36", "COM37", "COM38", "COM39", "COM40", "COM41", "COM42", "COM43", "COM44", "COM45", "COM46", "COM47", "COM48", "COM49", "COM50", "COM51", "COM52", "COM53", "COM54", "COM55", "COM56", "COM57", "COM58", "COM59", "COM60", "COM61", "COM62", "COM63", "COM64",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly ILogger<RegisterTemplateStore> _logger;

        public RegisterTemplateStore() : this(null, null)
        {
        }

        public RegisterTemplateStore(ILogger<RegisterTemplateStore>? logger, string? templatesDirectory = null)
        {
            _logger = logger ?? NullLogger<RegisterTemplateStore>.Instance;
            TemplatesDirectory = templatesDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ModbusForge",
                "templates");
        }

        public string TemplatesDirectory { get; }

        public string Save(RegisterTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);

            Directory.CreateDirectory(TemplatesDirectory);

            var name = string.IsNullOrWhiteSpace(template.Name) ? "template" : template.Name;
            var path = Path.Combine(TemplatesDirectory, $"{Sanitize(name)}.json");

            // Write to a temp file in the same directory and rename over the
            // destination, so a crash mid-write cannot truncate an existing
            // template into unreadable JSON.
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(template, SerializerOptions));
            File.Move(tmpPath, path, overwrite: true);

            _logger.LogInformation("Saved register template '{TemplateName}' with {EntryCount} entries to {Path}",
                template.Name, template.Entries.Count, path);
            return path;
        }

        /// <summary>
        /// Loads a template from <paramref name="filePath"/>. Returns null when the
        /// file does not exist, is unreadable, or does not contain a register
        /// template - callers can show the problem instead of catching exceptions.
        /// </summary>
        public RegisterTemplate? Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<RegisterTemplate>(json, SerializerOptions);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _logger.LogWarning(ex, "Failed to load register template '{Path}'", filePath);
                return null;
            }
        }

        public IReadOnlyList<string> ListTemplateFiles() =>
            Directory.Exists(TemplatesDirectory)
                ? Directory.GetFiles(TemplatesDirectory, "*.json").OrderBy(f => f).ToList()
                : Array.Empty<string>();

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

            // Trim trailing dots/spaces: "name." would become "name.json"
            // fine, but "name " -> "name .json" is ugly and "name." -> "name..json"
            // is ambiguous; also some Windows paths reject trailing dots.
            clean = clean.TrimEnd('.');
            clean = clean.TrimEnd();

            if (clean.Length == 0)
                clean = "template";

            // Truncate deterministically. A plain prefix can still be a
            // reserved stem ("con" + 240 chars of junk -> "con..."), and two
            // long names sharing a prefix would collide on the file name, so
            // over-long names keep a short prefix plus a hash of the full name:
            // stable across saves, unique per name, and never a reserved stem.
            if (clean.Length > MaxFileNameLength)
            {
                var hash = ComputeShortHash(name);
                var prefixLength = MaxFileNameLength - hash.Length - 1; // room for '-' + hash
                clean = clean[..prefixLength].TrimEnd('.').TrimEnd() + "-" + hash;
            }

            if (IsReservedFileName(clean))
                clean = clean + "_";

            return clean;
        }

        /// <summary>
        /// A stable, case-insensitive 12-hex-digit digest of the full (pre-sanitization)
        /// template name, used to keep truncated file names unique.
        /// </summary>
        private static string ComputeShortHash(string value)
        {
            var bytes = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes.AsSpan(0, 6)).ToLowerInvariant();
        }

        private static bool IsReservedFileName(string name)
        {
            var stem = name;
            // Reserved names match the file name WITHOUT extension on Windows.
            return ReservedFileNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
        }
    }
}

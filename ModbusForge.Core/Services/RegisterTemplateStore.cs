using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        RegisterTemplate Load(string filePath);

        IReadOnlyList<string> ListTemplateFiles();
    }

    /// <inheritdoc />
    public class RegisterTemplateStore : IRegisterTemplateStore
    {
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
            AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(template, SerializerOptions));

            _logger.LogInformation("Saved register template '{TemplateName}' with {EntryCount} entries to {Path}",
                template.Name, template.Entries.Count, path);
            return path;
        }

        public RegisterTemplate Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<RegisterTemplate>(json, SerializerOptions)
                ?? throw new InvalidDataException($"'{filePath}' does not contain a register template.");
        }

        public IReadOnlyList<string> ListTemplateFiles() =>
            Directory.Exists(TemplatesDirectory)
                ? Directory.GetFiles(TemplatesDirectory, "*.json").OrderBy(f => f).ToList()
                : Array.Empty<string>();

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}

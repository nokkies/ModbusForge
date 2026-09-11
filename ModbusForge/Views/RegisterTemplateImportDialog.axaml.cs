using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ModbusForge.Avalonia.Services;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Views
{
    public partial class RegisterTemplateImportDialog : Window
    {
        private readonly IRegisterTemplateImportService _importService;
        private readonly IFileDialogService _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IMessageBoxService? _messageBoxService;
        private RegisterTemplateImportResult? _result;
        private bool _isParsing;

        public RegisterTemplateImportDialog()
            : this(new RegisterTemplateImportService(), new AvaloniaFileDialogService(), new FileSystem())
        {
        }

        public RegisterTemplateImportDialog(
            IRegisterTemplateImportService importService,
            IFileDialogService fileDialogService,
            IFileSystem fileSystem,
            IMessageBoxService? messageBoxService = null,
            string? initialFilePath = null)
        {
            _importService = importService ?? throw new ArgumentNullException(nameof(importService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _messageBoxService = messageBoxService;

            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(initialFilePath))
            {
                FilePathBox.Text = initialFilePath;
                _ = ReparseAsync();
            }
        }

        public RegisterTemplate? ImportedTemplate => _result?.Template;

        public IReadOnlyList<RegisterTemplateEntry> Entries =>
            _result?.Entries ?? Array.Empty<RegisterTemplateEntry>();

        public bool SaveTemplate => SaveTemplateCheck.IsChecked == true;

        private AddressingConvention SelectedAddressing => AddressingCombo.SelectedIndex switch
        {
            1 => AddressingConvention.OneBased,
            2 => AddressingConvention.Modicon,
            _ => AddressingConvention.ZeroBased
        };

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void Browse_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            string? path;
            try
            {
                path = await _fileDialogService.ShowOpenFileDialogAsync(
                    "Select a register map",
                    "Register maps (*.csv;*.tsv;*.txt;*.xlsx;*.l5x;*.json;*.yaml;*.yml)|*.csv;*.tsv;*.txt;*.xlsx;*.l5x;*.json;*.yaml;*.yml|" +
                    "CSV files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Excel files (*.xlsx)|*.xlsx|" +
                    "Rockwell L5X (*.l5x)|*.l5x|JSON (*.json)|*.json|YAML (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*");
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
            {
                // async void would otherwise swallow the exception silently.
                SummaryText.Text = $"File dialog failed: {ex.Message}";
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
                return;

            FilePathBox.Text = path;
            await ReparseAsync();
        }

        private async void Addressing_Changed(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FilePathBox?.Text) || _isParsing)
                return;

            await ReparseAsync();
        }

        private async Task ReparseAsync()
        {
            var path = FilePathBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || _isParsing)
                return;

            _isParsing = true;
            try
            {
                ImportButton.IsEnabled = false;
                SummaryText.Text = "Reading file...";

                if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase) &&
                    !_fileSystem.FileExists(path))
                {
                    throw new FileNotFoundException("The selected file could not be found.", path);
                }

                _result = await ParseFileAsync(path, SelectedAddressing);
                _result.Template.SourceFile = path;
                if (string.IsNullOrWhiteSpace(_result.Template.Name))
                    _result.Template.Name = Path.GetFileNameWithoutExtension(path);

                if (string.IsNullOrWhiteSpace(TemplateNameBox.Text))
                    TemplateNameBox.Text = _result.Template.Name;

                PreviewGrid.ItemsSource = BuildPreviewRows(_result);
                ImportButton.IsEnabled = _result.Entries.Count > 0;
                SummaryText.Text =
                    $"{_result.RowsRead} row(s) read — {_result.Entries.Count} importable, " +
                    $"{_result.Errors.Count} rejected, {_result.Warnings.Count} warning(s).";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
            {
                _result = null;
                PreviewGrid.ItemsSource = null;
                ImportButton.IsEnabled = false;
                SummaryText.Text = string.Empty;
                if (_messageBoxService != null)
                {
                    await _messageBoxService.ShowAsync(
                        $"Could not read '{Path.GetFileName(path)}': {ex.Message}",
                        "Import Failed",
                        DialogButton.Ok,
                        DialogIcon.Error);
                }
                else
                {
                    SummaryText.Text = $"Could not read '{Path.GetFileName(path)}': {ex.Message}";
                }
            }
            finally
            {
                _isParsing = false;
            }
        }

        private async Task<RegisterTemplateImportResult> ParseFileAsync(string path, AddressingConvention addressing)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".xlsx")
                return await Task.Run(() => _importService.ImportFromFile(path, addressing));

            var text = await _fileSystem.ReadAllTextAsync(path);
            if (_importService is RegisterTemplateImportService concreteService)
            {
                return extension switch
                {
                    ".json" => concreteService.ImportJson(text, addressing),
                    ".yaml" or ".yml" => concreteService.ImportYaml(text, addressing),
                    ".l5x" => concreteService.ImportL5X(text, addressing),
                    _ => concreteService.ImportCsv(new StringReader(text), addressing)
                };
            }

            if (extension is ".csv" or ".tsv" or ".txt")
                return _importService.ImportCsv(new StringReader(text), addressing);

            return await Task.Run(() => _importService.ImportFromFile(path, addressing));
        }

        private static List<PreviewRow> BuildPreviewRows(RegisterTemplateImportResult result)
        {
            var rows = new List<PreviewRow>();

            foreach (var group in result.Errors.GroupBy(error => error.RowNumber).OrderBy(group => group.Key))
            {
                rows.Add(new PreviewRow
                {
                    RowNumber = group.Key,
                    Status = "Error",
                    Issues = string.Join("; ", group.Select(issue => issue.Message))
                });
            }

            var warningsByRow = result.Warnings.ToLookup(warning => warning.RowNumber);
            foreach (var entry in result.Entries)
            {
                var warnings = warningsByRow[entry.SourceRow].ToList();
                rows.Add(new PreviewRow
                {
                    RowNumber = entry.SourceRow,
                    Status = warnings.Count == 0 ? "Ok" : "Warning",
                    TagName = entry.TagName,
                    RegisterType = entry.RegisterType.ToString(),
                    RawAddress = entry.RawAddress,
                    Address = entry.Address,
                    Bit = entry.Bit,
                    DataType = entry.DataType.ToString(),
                    Length = entry.Length,
                    WordOrder = entry.WordOrder.ToString(),
                    Scale = entry.Scale.ToString(CultureInfo.CurrentCulture),
                    Offset = entry.Offset.ToString(CultureInfo.CurrentCulture),
                    Unit = entry.Unit,
                    Access = entry.Access.ToString(),
                    Enum = entry.FormatEnum(),
                    Issues = string.Join("; ", warnings.Select(warning => warning.Message))
                });
            }

            return rows.OrderBy(row => row.RowNumber).ToList();
        }

        private void Import_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_result == null || _result.Entries.Count == 0)
                return;

            if (!string.IsNullOrWhiteSpace(TemplateNameBox.Text))
                _result.Template.Name = TemplateNameBox.Text.Trim();

            Close(true);
        }

        private void Cancel_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

        public sealed class PreviewRow
        {
            public int RowNumber { get; init; }
            public string Status { get; init; } = "Ok";
            public string TagName { get; init; } = string.Empty;
            public string RegisterType { get; init; } = string.Empty;
            public string RawAddress { get; init; } = string.Empty;
            public int? Address { get; init; }
            public int? Bit { get; init; }
            public string DataType { get; init; } = string.Empty;
            public int Length { get; init; }
            public string WordOrder { get; init; } = string.Empty;
            public string Scale { get; init; } = string.Empty;
            public string Offset { get; init; } = string.Empty;
            public string Unit { get; init; } = string.Empty;
            public string Access { get; init; } = string.Empty;
            public string Enum { get; init; } = string.Empty;
            public string Issues { get; init; } = string.Empty;
        }
    }
}

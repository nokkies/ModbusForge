using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Views
{
    /// <summary>
    /// Preview dialog for importing a vendor register map / device template.
    /// Rows that failed validation are highlighted and excluded from the import.
    /// </summary>
    public partial class RegisterTemplateImportDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly IRegisterTemplateImportService _importService;
        private readonly IDialogService _dialogService;

        private RegisterTemplateImportResult? _result;

        public RegisterTemplateImportDialog(
            IRegisterTemplateImportService importService,
            IDialogService? dialogService = null,
            string? initialFilePath = null)
        {
            InitializeComponent();
            _importService = importService;
            _dialogService = dialogService ?? new NullDialogService();

            if (!string.IsNullOrWhiteSpace(initialFilePath))
            {
                FilePathBox.Text = initialFilePath;
                Reparse();
            }
        }

        /// <summary>The parsed template, available after the dialog is accepted.</summary>
        public RegisterTemplate? ImportedTemplate => _result?.Template;

        /// <summary>Entries the user chose to import (all rows that parsed successfully).</summary>
        public IReadOnlyList<RegisterTemplateEntry> Entries =>
            _result?.Entries ?? (IReadOnlyList<RegisterTemplateEntry>)Array.Empty<RegisterTemplateEntry>();

        /// <summary>Whether the template should also be saved to the templates folder.</summary>
        public bool SaveTemplate => SaveTemplateCheck.IsChecked == true;

        private AddressingConvention SelectedAddressing => AddressingCombo.SelectedIndex switch
        {
            1 => AddressingConvention.OneBased,
            2 => AddressingConvention.Modicon,
            _ => AddressingConvention.ZeroBased,
        };

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Register maps (*.csv;*.tsv;*.txt;*.xlsx;*.l5x;*.json;*.yaml;*.yml)|*.csv;*.tsv;*.txt;*.xlsx;*.l5x;*.json;*.yaml;*.yml|" +
                         "CSV files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|" +
                         "Excel files (*.xlsx)|*.xlsx|" +
                         "Rockwell L5X (*.l5x)|*.l5x|" +
                         "JSON (*.json)|*.json|" +
                         "YAML (*.yaml;*.yml)|*.yaml;*.yml|" +
                         "All files (*.*)|*.*",
                Title = "Select a register map"
            };

            if (dialog.ShowDialog() != true)
                return;

            FilePathBox.Text = dialog.FileName;
            Reparse();
        }

        private void Addressing_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsLoaded && !string.IsNullOrWhiteSpace(FilePathBox.Text))
                Reparse();
        }

        private void Reparse()
        {
            var path = FilePathBox.Text;
            try
            {
                _result = _importService.ImportFromFile(path, SelectedAddressing);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _result = null;
                PreviewGrid.ItemsSource = null;
                ImportButton.IsEnabled = false;
                SummaryText.Text = string.Empty;
                _dialogService.Show($"Could not read '{Path.GetFileName(path)}': {ex.Message}",
                    "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(TemplateNameBox.Text))
                TemplateNameBox.Text = _result.Template.Name;

            PreviewGrid.ItemsSource = BuildPreviewRows(_result);
            ImportButton.IsEnabled = _result.Entries.Count > 0;
            SummaryText.Text =
                $"{_result.RowsRead} row(s) read — {_result.Entries.Count} importable, " +
                $"{_result.Errors.Count} rejected, {_result.Warnings.Count} warning(s).";
        }

        private static List<PreviewRow> BuildPreviewRows(RegisterTemplateImportResult result)
        {
            var rows = new List<PreviewRow>();

            // Rejected rows first so problems are immediately visible.
            foreach (var group in result.Errors.GroupBy(err => err.RowNumber).OrderBy(g => g.Key))
            {
                rows.Add(new PreviewRow
                {
                    RowNumber = group.Key,
                    Status = "Error",
                    Issues = string.Join("; ", group.Select(i => i.Message)),
                });
            }

            var warningsByRow = result.Warnings.ToLookup(w => w.RowNumber);

            foreach (var entry in result.Entries)
            {
                var rowNumber = entry.SourceRow;
                var warnings = warningsByRow[rowNumber].ToList();

                rows.Add(new PreviewRow
                {
                    RowNumber = rowNumber,
                    Status = warnings.Count > 0 ? "Warning" : "Ok",
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
                    Issues = string.Join("; ", warnings.Select(w => w.Message)),
                });
            }

            return rows.OrderBy(r => r.RowNumber).ToList();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null || _result.Entries.Count == 0)
                return;

            if (!string.IsNullOrWhiteSpace(TemplateNameBox.Text))
                _result.Template.Name = TemplateNameBox.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private sealed class PreviewRow
        {
            public int RowNumber { get; init; }
            public string Status { get; init; } = "Ok";
            public string TagName { get; init; } = string.Empty;
            public string RegisterType { get; init; } = string.Empty;
            public string RawAddress { get; init; } = string.Empty;
            public int? Address { get; init; }
            public int? Bit { get; init; }
            public string DataType { get; init; } = string.Empty;
            public int? Length { get; init; }
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

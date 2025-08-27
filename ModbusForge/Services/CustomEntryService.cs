using ModbusForge.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace ModbusForge.Services
{
    public class CustomEntryService : ICustomEntryService
    {
        private readonly IFileDialogService _fileDialogService;

        public CustomEntryService(IFileDialogService fileDialogService)
        {
            _fileDialogService = fileDialogService;
        }

        public async Task<ObservableCollection<CustomEntry>> LoadCustomAsync()
        {
            var path = _fileDialogService.ShowOpenFileDialog("Load Custom Entries", "JSON files (*.json)|*.json|All files (*.*)|*.*");
            if (path is null)
            {
                return new ObservableCollection<CustomEntry>();
            }

            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            var list = new ObservableCollection<CustomEntry>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var ce = new CustomEntry
                {
                    Name = item.TryGetProperty("Name", out var nm) ? nm.GetString() ?? string.Empty : string.Empty,
                    Address = item.GetProperty("Address").GetInt32(),
                    Type = item.TryGetProperty("Type", out var t) ? t.GetString() ?? "uint" : "uint",
                    Value = item.TryGetProperty("Value", out var v) ? v.GetString() ?? "0" : "0",
                    Continuous = item.TryGetProperty("Continuous", out var c) && c.GetBoolean(),
                    PeriodMs = item.TryGetProperty("PeriodMs", out var p) ? p.GetInt32() : 1000,
                    Monitor = item.TryGetProperty("Monitor", out var mr) && mr.GetBoolean(),
                    ReadPeriodMs = item.TryGetProperty("ReadPeriodMs", out var rp) ? rp.GetInt32() : 1000,
                    Area = item.TryGetProperty("Area", out var a) ? a.GetString() ?? "HoldingRegister" : "HoldingRegister",
                    Trend = item.TryGetProperty("Trend", out var tr) && tr.GetBoolean()
                };
                list.Add(ce);
            }
            return list;
        }

        public async Task SaveCustomAsync(ObservableCollection<CustomEntry> entries)
        {
            var path = _fileDialogService.ShowSaveFileDialog("Save Custom Entries", "JSON files (*.json)|*.json|All files (*.*)|*.*", "custom-entries.json");
            if (path is null)
            {
                return;
            }

            var data = new System.Collections.Generic.List<object>();
            foreach (var e in entries)
            {
                data.Add(new
                {
                    e.Name,
                    e.Address,
                    e.Type,
                    e.Value,
                    e.Continuous,
                    e.PeriodMs,
                    e.Monitor,
                    e.ReadPeriodMs,
                    e.Area,
                    e.Trend
                });
            }
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            await File.WriteAllTextAsync(path, json);
        }
    }
}

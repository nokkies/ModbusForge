using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the bulk-add custom watch dialog.
    /// </summary>
    public sealed partial class CustomBulkAddDialogViewModel : ObservableObject
    {
        private readonly TaskCompletionSource<CustomBulkAddDialogResult?> _tcs = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int _startRegister = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int _count = 10;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private string _selectedArea = "HoldingRegister";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private string _selectedType = "int";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int _readPeriodMs = 1000;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int _writePeriodMs = 1000;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private string _namePrefix = "Tag";

        public CustomBulkAddDialogViewModel()
        {
            OkCommand = new RelayCommand(Ok, CanOk);
            CancelCommand = new RelayCommand(Cancel);
        }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public IReadOnlyList<string> Areas { get; } = CustomEntry.AvailableAreas;

        public IReadOnlyList<string> Types { get; } = CustomEntry.AvailableTypes;

        public Task<CustomBulkAddDialogResult?> ResultTask => _tcs.Task;

        public event EventHandler? RequestClose;

        private void Ok()
        {
            if (!CanOk()) return;

            var result = new CustomBulkAddDialogResult(
                StartRegister,
                Count,
                SelectedArea,
                SelectedType,
                ReadPeriodMs,
                WritePeriodMs,
                NamePrefix);
            _tcs.TrySetResult(result);
            Close();
        }

        private void Cancel()
        {
            _tcs.TrySetResult(null);
            Close();
        }

        private bool CanOk()
        {
            if (Count < 1) return false;
            if (StartRegister < 0) return false;
            if (ReadPeriodMs < 1) return false;
            if (WritePeriodMs < 1) return false;
            if (string.IsNullOrWhiteSpace(NamePrefix)) return false;
            if (string.IsNullOrWhiteSpace(SelectedArea)) return false;
            if (string.IsNullOrWhiteSpace(SelectedType)) return false;
            return true;
        }

        public void Close()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}

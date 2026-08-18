using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Avalonia.Services;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the "Add Trend Pen" dialog: pick a register (area +
    /// address) or an existing tag, give the pen a name and a read period.
    /// </summary>
    public sealed partial class TrendAddDialogViewModel : ObservableObject
    {
        private readonly TagService? _tagService;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private bool _isTagSource;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private string _selectedArea = "HoldingRegister";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int _address = 1;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private Tag? _selectedTag;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private string _name = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(OkCommand))]
        private int _readPeriodMs = 1000;

        /// <summary>True once the user edits the name manually; suppresses auto-fill.</summary>
        private bool _nameTouched;

        /// <summary>Set while auto-fill writes the name, so the change handler
        /// does not mistake it for a user edit.</summary>
        private bool _autoFillingName;

        public ObservableCollection<Tag> TrendableTags { get; } = new();

        public IReadOnlyList<string> Areas { get; } = CustomEntry.AvailableAreas;

        public TrendAddDialogViewModel(TagService? tagService = null)
        {
            _tagService = tagService;
            if (_tagService != null)
            {
                foreach (var tag in _tagService.Tags)
                {
                    // Bit-packed tags read a single bit of a register; the
                    // trend pipeline samples whole-register values, so they
                    // cannot be pens.
                    if (tag.Bit is null) TrendableTags.Add(tag);
                }
            }

            OkCommand = new RelayCommand(Ok, CanOk);
            CancelCommand = new RelayCommand(Cancel);

            if (TrendableTags.Count > 0) SelectedTag = TrendableTags[0];
            RefreshName();
        }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public event EventHandler? RequestClose;

        partial void OnIsTagSourceChanged(bool value)
        {
            if (SelectedTag is null && TrendableTags.Count > 0) SelectedTag = TrendableTags[0];
            RefreshName();
            OkCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedTagChanged(Tag? value)
        {
            if (!IsTagSource && value != null)
            {
                IsTagSource = true;
            }
            RefreshName();
            OkCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedAreaChanged(string value)
        {
            if (!IsTagSource) RefreshName();
        }

        partial void OnAddressChanged(int value)
        {
            if (!IsTagSource) RefreshName();
        }

        partial void OnNameChanged(string value)
        {
            if (!_autoFillingName)
            {
                _nameTouched = true;
                OkCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// Auto-fills the suggested name until the user edits it:
        /// tag source → the tag's name; register source → "HR Trend 1" style.
        /// </summary>
        private void RefreshName()
        {
            if (_nameTouched) return;

            _autoFillingName = true;
            try
            {
                if (IsTagSource && SelectedTag is { } tag)
                {
                    Name = tag.Name;
                }
                else
                {
                    var prefix = SelectedArea switch
                    {
                        "HoldingRegister" => "HR",
                        "InputRegister" => "IR",
                        _ => SelectedArea
                    };
                    Name = $"{prefix} Trend {Address}";
                }
            }
            finally
            {
                _autoFillingName = false;
            }
        }

        private bool CanOk()
        {
            if (string.IsNullOrWhiteSpace(Name)) return false;
            if (IsTagSource) return SelectedTag is not null;
            return Address >= 0 && ReadPeriodMs > 0;
        }

        private void Ok()
        {
            if (!CanOk()) return;

            TrendAddDialogResult result;
            if (IsTagSource && SelectedTag is { } tag)
            {
                result = new TrendAddDialogResult(
                    tag.Area.ToString(),
                    tag.Address,
                    Name.Trim(),
                    ReadPeriodMs);
            }
            else
            {
                result = new TrendAddDialogResult(
                    SelectedArea,
                    Address,
                    Name.Trim(),
                    ReadPeriodMs);
            }

            RequestClose?.Invoke(this, new TrendAddDialogResultEventArgs(result));
        }

        private void Cancel()
        {
            RequestClose?.Invoke(this, new TrendAddDialogResultEventArgs(null));
        }
    }

    public sealed class TrendAddDialogResultEventArgs(TrendAddDialogResult? result) : EventArgs
    {
        public TrendAddDialogResult? Result { get; } = result;
    }
}

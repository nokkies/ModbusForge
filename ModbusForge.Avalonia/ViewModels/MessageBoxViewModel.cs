using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the Avalonia message box window.
    /// </summary>
    public sealed partial class MessageBoxViewModel : ObservableObject
    {
        private readonly TaskCompletionSource<DialogResult> _tcs = new();

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _message = string.Empty;

        [ObservableProperty]
        private string _primaryText = string.Empty;

        [ObservableProperty]
        private string _secondaryText = string.Empty;

        [ObservableProperty]
        private string _tertiaryText = string.Empty;

        [ObservableProperty]
        private bool _isPrimaryDefault;

        [ObservableProperty]
        private bool _isSecondaryDefault;

        [ObservableProperty]
        private bool _isTertiaryDefault;

        public MessageBoxViewModel(string title, string message, DialogButton button, DialogIcon icon)
        {
            Title = title;
            Message = message;

            switch (button)
            {
                case DialogButton.Ok:
                    PrimaryText = "OK";
                    IsPrimaryDefault = true;
                    PrimaryCommand = new RelayCommand(() => Close(DialogResult.Ok));
                    break;

                case DialogButton.OkCancel:
                    PrimaryText = "OK";
                    SecondaryText = "Cancel";
                    IsPrimaryDefault = true;
                    PrimaryCommand = new RelayCommand(() => Close(DialogResult.Ok));
                    SecondaryCommand = new RelayCommand(() => Close(DialogResult.Cancel));
                    break;

                case DialogButton.YesNo:
                    PrimaryText = "Yes";
                    SecondaryText = "No";
                    IsPrimaryDefault = true;
                    PrimaryCommand = new RelayCommand(() => Close(DialogResult.Yes));
                    SecondaryCommand = new RelayCommand(() => Close(DialogResult.No));
                    break;

                case DialogButton.YesNoCancel:
                    PrimaryText = "Yes";
                    SecondaryText = "No";
                    TertiaryText = "Cancel";
                    IsPrimaryDefault = true;
                    PrimaryCommand = new RelayCommand(() => Close(DialogResult.Yes));
                    SecondaryCommand = new RelayCommand(() => Close(DialogResult.No));
                    TertiaryCommand = new RelayCommand(() => Close(DialogResult.Cancel));
                    break;
            }

            _ = icon; // reserved for future icon styling
        }

        public IRelayCommand? PrimaryCommand { get; }
        public IRelayCommand? SecondaryCommand { get; }
        public IRelayCommand? TertiaryCommand { get; }

        public Task<DialogResult> ResultTask => _tcs.Task;

        public event EventHandler? RequestClose;

        public void TrySetNone()
        {
            _tcs.TrySetResult(DialogResult.None);
        }

        private void Close(DialogResult result)
        {
            _tcs.TrySetResult(result);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}

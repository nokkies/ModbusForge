using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// View model for a simple prompt dialog with a text box.
    /// </summary>
    public sealed partial class InputDialogViewModel : ObservableObject
    {
        private readonly TaskCompletionSource<string?> _tcs = new();

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _prompt = string.Empty;

        [ObservableProperty]
        private string _input = string.Empty;

        public InputDialogViewModel(string title, string prompt, string defaultValue)
        {
            Title = title;
            Prompt = prompt;
            Input = defaultValue;

            OkCommand = new RelayCommand(() =>
            {
                _tcs.TrySetResult(Input);
                Close();
            });

            CancelCommand = new RelayCommand(() =>
            {
                _tcs.TrySetResult(null);
                Close();
            });
        }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public Task<string?> ResultTask => _tcs.Task;

        public event EventHandler? RequestClose;

        public void Close()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}

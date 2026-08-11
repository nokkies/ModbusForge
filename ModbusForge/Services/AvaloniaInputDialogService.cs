using System;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Threading;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Avalonia input prompt implementation. Uses a nested dispatcher frame to provide a
    /// synchronous API while the underlying Window.ShowDialog is async.
    /// </summary>
    public sealed class AvaloniaInputDialogService : IInputDialogService
    {
        private readonly record struct InputResult(bool Accepted, string Value);

        public bool TryGetInput(string title, string prompt, string defaultValue, out string input)
        {
            InputResult result;

            if (!Dispatcher.UIThread.CheckAccess())
            {
                result = Dispatcher.UIThread.Invoke(() => TryGetInputCore(title, prompt, defaultValue));
            }
            else
            {
                result = TryGetInputCore(title, prompt, defaultValue);
            }

            input = result.Value;
            return result.Accepted;
        }

        private static InputResult TryGetInputCore(string title, string prompt, string defaultValue)
        {
            var tcs = new TaskCompletionSource<string?>();
            var frame = new DispatcherFrame(false);

            var viewModel = new InputDialogViewModel(title, prompt, defaultValue);

            viewModel.RequestClose += (sender, e) =>
            {
                tcs.TrySetResult(viewModel.ResultTask.IsCompleted ? viewModel.ResultTask.Result : null);
                frame.Continue = false;
            };

            var window = new InputDialogWindow
            {
                DataContext = viewModel
            };

            window.Closed += (sender, e) =>
            {
                tcs.TrySetResult(viewModel.ResultTask.IsCompleted ? viewModel.ResultTask.Result : null);
                frame.Continue = false;
            };

            var owner = GetMainWindow();
            if (owner != null)
            {
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }

            Dispatcher.UIThread.PushFrame(frame);

            var result = tcs.Task.GetAwaiter().GetResult();
            return result == null
                ? new InputResult(false, defaultValue)
                : new InputResult(true, result);
        }

        private static Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            return null;
        }
    }
}

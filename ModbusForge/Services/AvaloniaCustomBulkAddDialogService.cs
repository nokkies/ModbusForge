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
    /// Avalonia bulk-add dialog implementation. Uses a nested dispatcher frame to provide a
    /// synchronous API while the underlying Window.ShowDialog is async.
    /// </summary>
    public sealed class AvaloniaCustomBulkAddDialogService : ICustomBulkAddDialogService
    {
        public bool TryGetBulkAdd(out CustomBulkAddDialogResult? result)
        {
            CustomBulkAddDialogResult? localResult = null;

            if (!Dispatcher.UIThread.CheckAccess())
            {
                localResult = Dispatcher.UIThread.Invoke(TryGetBulkAddCore);
                result = localResult;
                return localResult != null;
            }

            localResult = TryGetBulkAddCore();
            result = localResult;
            return localResult != null;
        }

        private static CustomBulkAddDialogResult? TryGetBulkAddCore()
        {
            var tcs = new TaskCompletionSource<CustomBulkAddDialogResult?>();
            var frame = new DispatcherFrame(false);

            var viewModel = new CustomBulkAddDialogViewModel();

            viewModel.RequestClose += (sender, e) =>
            {
                tcs.TrySetResult(viewModel.ResultTask.IsCompleted ? viewModel.ResultTask.Result : null);
                frame.Continue = false;
            };

            var window = new CustomBulkAddDialogWindow
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
            return result;
        }

        private static Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;

            return null;
        }
    }
}

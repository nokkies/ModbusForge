using System;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Threading;
using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Avalonia "Add Trend Pen" dialog implementation. Uses a nested dispatcher
    /// frame to provide a synchronous API while the underlying Window.ShowDialog
    /// is async (same pattern as <see cref="AvaloniaCustomBulkAddDialogService"/>).
    /// </summary>
    public sealed class AvaloniaTrendAddDialogService : ITrendAddDialogService
    {
        private readonly TagService _tagService;

        public AvaloniaTrendAddDialogService(TagService tagService)
        {
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
        }

        public TrendAddDialogResult? TryGetAddTrendPen()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                return Dispatcher.UIThread.Invoke(TryGetAddTrendPenCore);
            }

            return TryGetAddTrendPenCore();
        }

        private TrendAddDialogResult? TryGetAddTrendPenCore()
        {
            var tcs = new TaskCompletionSource<TrendAddDialogResult?>();
            var frame = new DispatcherFrame(false);

            var viewModel = new TrendAddDialogViewModel(_tagService);

            viewModel.RequestClose += (sender, e) =>
            {
                tcs.TrySetResult(e is TrendAddDialogResultEventArgs args ? args.Result : null);
                frame.Continue = false;
            };

            var window = new TrendAddDialogWindow
            {
                DataContext = viewModel
            };

            window.Closed += (sender, e) =>
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(null);
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

            return tcs.Task.GetAwaiter().GetResult();
        }

        private static Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;

            return null;
        }
    }
}

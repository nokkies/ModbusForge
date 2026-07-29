using System;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Avalonia implementation of a cross-platform message box.
    /// </summary>
    public sealed class AvaloniaMessageBoxService : IMessageBoxService
    {
        public async Task<DialogResult> ShowAsync(string message, string title, DialogButton button, DialogIcon icon)
        {
            var viewModel = new MessageBoxViewModel(title, message, button, icon);
            var window = new MessageBoxWindow
            {
                DataContext = viewModel
            };

            viewModel.RequestClose += (sender, e) => window.Close();
            window.Closed += (sender, e) => viewModel.TrySetNone();

            var owner = GetOwner();
            if (owner != null)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }

            return await viewModel.ResultTask;
        }

        private static Window? GetOwner()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            return null;
        }
    }
}

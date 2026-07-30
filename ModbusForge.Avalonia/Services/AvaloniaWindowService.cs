using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    public sealed class AvaloniaWindowService : IWindowService
    {
        private readonly IServiceProvider _serviceProvider;

        public AvaloniaWindowService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        private static Window? GetOwner()
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        public void ShowPreferences()
        {
            var settings = _serviceProvider.GetRequiredService<ISettingsService>();
            var messageBox = _serviceProvider.GetService<IMessageBoxService>();
            var viewModel = new PreferencesViewModel(settings, messageBox);
            var window = new PreferencesWindow(viewModel);
            window.Show();
        }

        public void ShowAbout()
        {
            var window = new AboutWindow();
            window.Show();
        }

        public void ShowHelp(string? topic = null)
        {
            var helpService = _serviceProvider.GetService<IHelpContentService>();
            var text = topic is not null && helpService is not null
                ? helpService.GetHelpContent(topic) ?? "Help content not available."
                : "Welcome to ModbusForge. Use the tabs to connect, read, write, and visualize Modbus data.";

            var window = new HelpWindow(text);
            window.Show();
        }

        public void ShowKeyboardShortcuts()
        {
            var window = new KeyboardShortcutsWindow();
            window.Show();
        }

        public void ShowTroubleshooting()
        {
            var window = new TroubleshootingWindow();
            window.Show();
        }
    }
}

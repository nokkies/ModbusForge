using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            var helpService = _serviceProvider.GetRequiredService<IHelpContentService>();
            var logger = _serviceProvider.GetRequiredService<ILogger<HelpViewModel>>();
            var viewModel = new HelpViewModel(helpService, logger);

            if (!string.IsNullOrWhiteSpace(topic))
            {
                viewModel.NavigateCommand.Execute(topic);
            }

            var window = new HelpWindow(viewModel);
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

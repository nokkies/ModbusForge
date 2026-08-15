using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Views
{
    public partial class MainWindow : global::Avalonia.Controls.Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (ViewModel?.CheckForUpdatesCommand is not IAsyncRelayCommand asyncCmd) return;

            var app = global::Avalonia.Application.Current as App;
            var settingsService = app?.Services?.GetService<ISettingsService>();
            if (settingsService?.CheckForUpdatesOnStartup != true) return;

            try
            {
                await asyncCmd.ExecuteAsync(null);
            }
            catch (Exception)
            {
                // Best-effort startup update check
            }
        }

        private void AdvancedFunctions_Click(object? sender, RoutedEventArgs e)
        {
            var app = global::Avalonia.Application.Current as App;
            var vm = ViewModel;
            if (app?.Services == null || vm?.ActiveService == null)
            {
                if (vm != null) vm.StatusMessage = "Connect a profile before opening Advanced Functions.";
                return;
            }

            var window = new AdvancedFunctionsWindow(
                new AdvancedFunctionsViewModel(
                    vm.ActiveService,
                    vm.EffectiveUnitId,
                    app.Services.GetRequiredService<ILogger<AdvancedFunctionsViewModel>>()));

            _ = window.ShowDialog(this);
        }

        private void DeviceScanner_Click(object? sender, RoutedEventArgs e)
        {
            var app = global::Avalonia.Application.Current as App;
            if (app?.Services == null) return;

            var scanner = app.Services.GetRequiredService<IDeviceScannerService>();
            var connectionManager = app.Services.GetRequiredService<IConnectionManager>();
            var dispatcher = app.Services.GetRequiredService<ModbusForge.Services.IDispatcher>();
            var fileDialogService = app.Services.GetRequiredService<IFileDialogService>();
            var messageBoxService = app.Services.GetRequiredService<IMessageBoxService>();
            var fileSystem = app.Services.GetRequiredService<IFileSystem>();

            var window = new DeviceScannerWindow
            {
                DataContext = new DeviceScannerViewModel(
                    scanner,
                    connectionManager,
                    dispatcher,
                    fileDialogService,
                    messageBoxService,
                    fileSystem,
                    app.Services.GetRequiredService<ILogger<DeviceScannerViewModel>>())
            };

            _ = window.ShowDialog(this);
        }
    }
}

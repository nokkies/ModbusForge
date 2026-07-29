using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Views
{
    public partial class MainView : global::Avalonia.Controls.UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        private void ManageConnections_Click(object? sender, RoutedEventArgs e)
        {
            var app = global::Avalonia.Application.Current as App;
            if (app?.Services == null) return;

            var connectionManager = app.Services.GetRequiredService<IConnectionManager>();
            var dispatcher = app.Services.GetRequiredService<ModbusForge.Services.IDispatcher>();

            var window = new ConnectionManagerWindow
            {
                DataContext = new ConnectionManagerViewModel(connectionManager, dispatcher)
            };

            var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel is global::Avalonia.Controls.Window owner)
            {
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
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

            var topLevel = global::Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel is global::Avalonia.Controls.Window owner)
            {
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
        }
    }
}

using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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
    }
}

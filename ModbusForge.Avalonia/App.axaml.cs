using System;
using global::Avalonia;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Avalonia.Views;
using ModbusForge.Services;

namespace ModbusForge.Avalonia
{
    public partial class App : global::Avalonia.Application
    {
        public IServiceProvider? Services { get; private set; }

        public override void Initialize()
        {
            global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Services = ConfigureServices();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainViewModel>()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            }));

            // Avalonia platform services
            services.AddSingleton<ModbusForge.Services.IDispatcher, AvaloniaDispatcher>();
            services.AddSingleton<ModbusForge.Services.IApplicationLifetime, AvaloniaApplicationLifetime>();
            services.AddSingleton<IThemeService, AvaloniaThemeService>();
            services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
            services.AddSingleton<IInputDialogService, AvaloniaInputDialogService>();
            services.AddSingleton<IMessageBoxService, AvaloniaMessageBoxService>();

            // Connection management
            services.AddSingleton<IValidationService, ValidationService>();
            services.AddSingleton<IConnectionManager, ConnectionManager>();

            // File system & file dialogs
            services.AddSingleton<IFileSystem, FileSystem>();

            // Device scanner
            services.AddSingleton<IDeviceIdentificationReader, DeviceIdentificationReader>();
            services.AddSingleton<IModbusDeviceProbe, ModbusDeviceProbe>();
            services.AddSingleton<IDeviceScannerService, DeviceScannerService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();

            return services.BuildServiceProvider();
        }
    }
}

using System;
using global::Avalonia;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Avalonia.Services;
using ModbusForge.Configuration;
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

            // Settings and MQTT gateway
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<MqttGatewayService>();

            // Device scanner
            services.AddSingleton<IDeviceIdentificationReader, DeviceIdentificationReader>();
            services.AddSingleton<IModbusDeviceProbe, ModbusDeviceProbe>();
            services.AddSingleton<IDeviceScannerService, DeviceScannerService>();

            // Custom entries
            services.AddSingleton<ICustomEntryService, CustomEntryService>();

            // Trend logging
            services.Configure<LoggingSettings>(_ => { });
            services.AddSingleton<ITrendLogger, TrendLoggingService>();

            // Frame inspector & pcap import
            services.AddSingleton<FrameInspectorViewModel>();
            services.AddSingleton<PcapImportService>();

            // Scripting & signal generator
            services.AddSingleton<IScriptRunner, ScriptRunner>();
            services.AddSingleton<ScriptEditorViewModel>();
            services.AddSingleton<SignalGeneratorViewModel>();

            // Visual simulation
            services.AddSingleton<IAvaloniaVisualSimulationService, AvaloniaVisualSimulationService>();
            services.AddSingleton<VisualNodeEditorViewModel>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<TrendViewModel>();
            services.AddSingleton<MqttViewModel>();

            return services.BuildServiceProvider();
        }
    }
}

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

            // File system, file dialogs, and per-Unit ID workspace state
            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<IUnitConfigurationStore, UnitConfigurationStore>();

            // Settings, help, update check and window service
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IHelpContentService, HelpContentService>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IWindowService, AvaloniaWindowService>();
            services.AddSingleton<MqttGatewayService>();

            // Device scanner
            services.AddSingleton<IDeviceIdentificationReader, DeviceIdentificationReader>();
            services.AddSingleton<IModbusDeviceProbe, ModbusDeviceProbe>();
            services.AddSingleton<IDeviceScannerService, DeviceScannerService>();

            // Custom entries and tag tools
            services.AddSingleton<ICustomEntryService, CustomEntryService>();
            services.AddSingleton<TagService>();
            services.AddSingleton<IRegisterTemplateImportService, RegisterTemplateImportService>();
            services.AddSingleton<IRegisterTemplateStore, RegisterTemplateStore>();
            services.AddSingleton<ITagWindowService, AvaloniaTagWindowService>();

            // Lightweight dock/float host for tool windows
            services.AddSingleton<AvaloniaDockingHost>();
            services.AddSingleton<IDockingHost>(sp => sp.GetRequiredService<AvaloniaDockingHost>());

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
            services.AddSingleton<IVisualSimulationService, AvaloniaVisualSimulationService>();
            services.AddSingleton<VisualNodeEditorViewModel>();

            // ViewModels
            services.AddSingleton<TrendViewModel>();
            services.AddSingleton<DecodeViewModel>();
            services.AddSingleton<MqttViewModel>();
            services.AddSingleton<MainViewModel>();

            return services.BuildServiceProvider();
        }
    }
}

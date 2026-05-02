using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using ModbusForge.ViewModels.Coordinators;

namespace ModbusForge
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IConfiguration? Configuration { get; private set; }
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        public App()
        {
            InitializeComponent();
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Set up global exception handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Create and show the main window
            var mainWindow = new MainWindow(ServiceProvider.GetRequiredService<MainViewModel>());
            mainWindow.Show();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogFatalException(e.Exception, "DispatcherUnhandledException");
            e.Handled = true; // Prevent immediate crash to allow logger to finish
            MessageBox.Show($"A fatal error occurred: {e.Exception.Message}\n\nDetails logged to crash.log", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogFatalException(ex, "AppDomain.UnhandledException");
            }
        }

        private void LogFatalException(Exception ex, string source)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var message = $"[{DateTime.Now}] FATAL ERROR ({source}): {ex}\n\n";
                File.AppendAllText(logPath, message);
            }
            catch { /* can't even log */ }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
            services.AddSingleton(Configuration);

            // Options
            services.AddOptions();
            services.Configure<ServerSettings>(Configuration.GetSection("ServerSettings"));
            services.Configure<LoggingSettings>(Configuration.GetSection("LoggingSettings"));

            // Configure logging
            services.AddLogging(configure => 
            {
                configure.AddConsole();
                configure.AddDebug();
                configure.SetMinimumLevel(LogLevel.Debug);
            });

            // Register services (both Client and Server). ViewModel selects at runtime.
            services.AddSingleton<ModbusTcpService>();
            services.AddSingleton<ModbusServerService>();
            services.AddSingleton<IModbusService>(provider => 
            {
                // Get the server settings to determine which service to use
                var settings = provider.GetRequiredService<IOptions<ServerSettings>>().Value;
                return string.Equals(settings.Mode, "Server", StringComparison.OrdinalIgnoreCase)
                    ? provider.GetRequiredService<ModbusServerService>()
                    : provider.GetRequiredService<ModbusTcpService>();
            });
            services.AddSingleton<ITrendLogger, TrendLoggingService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<ICustomEntryService, CustomEntryService>();
            services.AddSingleton<IConsoleLoggerService, ConsoleLoggerService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IConnectionManager, ConnectionManager>();
            services.AddSingleton<IScriptRunner, ScriptRunner>();
            services.AddSingleton<IScriptRuleService, ScriptRuleService>();
            services.AddSingleton<IVisualSimulationService, VisualSimulationService>();
            
            // Register Coordinators
            services.AddSingleton<ConnectionCoordinator>(provider => new ConnectionCoordinator(
                provider.GetRequiredService<ModbusTcpService>(),
                provider.GetRequiredService<ModbusServerService>(),
                provider.GetRequiredService<IConsoleLoggerService>(),
                provider.GetRequiredService<ILogger<ConnectionCoordinator>>()
            ));
            services.AddSingleton<RegisterCoordinator>();
            services.AddSingleton<CustomEntryCoordinator>();
            services.AddSingleton<TrendCoordinator>(provider => new TrendCoordinator(
                provider.GetRequiredService<ModbusTcpService>(),
                provider.GetRequiredService<ModbusServerService>(),
                provider.GetRequiredService<ITrendLogger>(),
                provider.GetRequiredService<ILogger<TrendCoordinator>>()
            ));
            services.AddSingleton<ConfigurationCoordinator>();
            
            // Register ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<TrendViewModel>();
            services.AddTransient<DecodeViewModel>();
            services.AddTransient<ScriptEditorViewModel>();
        }


        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up services
            if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }
}

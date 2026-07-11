using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;
using ModbusForge.Services;
using ModbusForge.Services.Api;
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

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // Set up global exception handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Validate configuration on startup
            var configValidator = ServiceProvider.GetRequiredService<IConfigurationValidator>();
            var serverSettings = ServiceProvider.GetRequiredService<IOptions<ServerSettings>>().Value;
            var loggingSettings = ServiceProvider.GetRequiredService<IOptions<LoggingSettings>>().Value;
            
            var validationResult = configValidator.ValidateConfiguration(serverSettings, loggingSettings);
            if (!validationResult.IsValid)
            {
                MessageBox.Show(
                    $"Configuration validation failed:\n{validationResult.ErrorMessage}\n\nApplication will exit.",
                    "Configuration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Log any configuration warnings
            var warnings = configValidator.GetValidationWarnings();
            if (warnings.Count > 0)
            {
                var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
                foreach (var warning in warnings)
                {
                    logger.LogWarning("Configuration warning: {Warning}", warning);
                }
            }

            // Initialize TagService (loads persisted data)
            var tagService = ServiceProvider.GetRequiredService<TagService>();
            await tagService.InitializeAsync();

            // Start API Service if enabled
            var settingsService = ServiceProvider.GetRequiredService<ISettingsService>();
            var apiServerService = ServiceProvider.GetRequiredService<IApiServerService>();
            if (settingsService.EnableApi)
            {
                await apiServerService.StartAsync();
            }

            // Create and show the main window
            var mainWindow = new MainWindow(ServiceProvider.GetRequiredService<MainViewModel>(), ServiceProvider);
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(mainWindow);
            mainWindow.Show();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogFatalException(e.Exception, "DispatcherUnhandledException");
            e.Handled = true; // Prevent immediate crash to allow logger to finish
            MessageBox.Show($"A fatal error occurred: {e.Exception.Message}\n\nDetails have been logged.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var logger = ServiceProvider.GetService<ILogger<App>>();
                if (logger != null)
                {
                    logger.LogError(ex, "FATAL ERROR ({Source}): {Message}", source, ex.Message);
                }
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
            services.AddSingleton<IDialogService, MessageBoxDialogService>();
            services.AddSingleton<ICustomEntryService, CustomEntryService>();
            services.AddSingleton<IDispatcher, WpfDispatcher>();
            services.AddSingleton<IConsoleLoggerService, ConsoleLoggerService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IConnectionManager, ConnectionManager>();
            services.AddSingleton<IScriptRunner, ScriptRunner>();
            services.AddSingleton<IScriptRuleService, ScriptRuleService>();
            services.AddSingleton<IVisualSimulationService, VisualSimulationService>();

            // API server: wire the focused facade (no WPF IServiceProvider passed in).
            // IAppStateAccessor is resolved lazily after MainViewModel is created.
            services.AddSingleton<IAppStateAccessor>(provider =>
                new MainViewModelAppStateAccessor(provider.GetRequiredService<MainViewModel>()));
            services.AddSingleton<IApiApplicationService>(provider =>
                new WpfApiApplicationService(
                    provider.GetRequiredService<IAppStateAccessor>(),
                    provider.GetRequiredService<IModbusService>(),
                    provider.GetRequiredService<IScriptRuleService>(),
                    provider.GetRequiredService<IConsoleLoggerService>(),
                    provider.GetRequiredService<ITrendLogger>(),
                    provider.GetRequiredService<IDispatcher>(),
                    provider.GetRequiredService<ILogger<WpfApiApplicationService>>()));
            services.AddSingleton<IApiServerService, ApiServerService>();

            services.AddSingleton<TagService>();
            services.AddSingleton<IRetryPolicyService, RetryPolicyService>();
            services.AddSingleton<IValidationService, ValidationService>();
            services.AddSingleton<IConfigurationValidator, ConfigurationValidator>();
            services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
            services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
            services.AddSingleton<IHelpContentService, HelpContentService>();
            services.AddSingleton<IWindowOwnerProvider, MainWindowOwnerProvider>();
            services.AddSingleton<ITagWindowService, TagWindowService>();
            
            // Register Coordinators
            services.AddSingleton<ConnectionCoordinator>(provider => new ConnectionCoordinator(
                provider.GetRequiredService<ModbusTcpService>(),
                provider.GetRequiredService<ModbusServerService>(),
                provider.GetRequiredService<IConsoleLoggerService>(),
                provider.GetRequiredService<ILogger<ConnectionCoordinator>>(),
                provider.GetRequiredService<IRetryPolicyService>(),
                provider.GetRequiredService<IValidationService>(),
                provider.GetRequiredService<IErrorHandlingService>(),
                provider.GetRequiredService<ICircuitBreakerService>(),
                provider.GetRequiredService<IDialogService>()
            ));
            services.AddSingleton<RegisterCoordinator>();
            services.AddSingleton<CustomEntryCoordinator>();
            services.AddSingleton<TrendCoordinator>(provider => new TrendCoordinator(
                provider.GetRequiredService<ModbusTcpService>(),
                provider.GetRequiredService<ModbusServerService>(),
                provider.GetRequiredService<ITrendLogger>(),
                provider.GetRequiredService<ILogger<TrendCoordinator>>(),
                provider.GetRequiredService<ISettingsService>()
            ));
            services.AddSingleton<ConfigurationCoordinator>();
            
            // Register ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<TrendViewModel>();
            services.AddTransient<DecodeViewModel>();
            services.AddTransient<VisualNodeEditorViewModel>();
            services.AddTransient<HelpViewModel>();
        }


        protected override async void OnExit(ExitEventArgs e)
        {
            // Stop API Service
            var apiServerService = ServiceProvider.GetService<IApiServerService>();
            if (apiServerService != null && apiServerService.IsRunning)
            {
                await apiServerService.StopAsync();
            }

            // Clean up services
            try
            {
                if (ServiceProvider is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (ServiceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                var logger = ServiceProvider.GetService<ILogger<App>>();
                logger?.LogDebug("Service provider disposal was canceled");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                var logger = ServiceProvider.GetService<ILogger<App>>();
                logger?.LogError(ex, "Error disposing service provider");
            }

            base.OnExit(e);
        }
    }
}

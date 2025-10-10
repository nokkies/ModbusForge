using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using System;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.Extensions.Options;
using ModbusForge.Configuration;

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
            // Create and show the main window
            var mainWindow = new MainWindow(ServiceProvider.GetRequiredService<MainViewModel>());
            mainWindow.Show();
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
            services.AddSingleton<ITrendLogger, TrendLoggingService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<ISimulationService, SimulationService>();
            services.AddSingleton<ICustomEntryService, CustomEntryService>();
            services.AddSingleton<IConsoleLoggerService, ConsoleLoggerService>();
            
            // Register ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<TrendViewModel>();
            services.AddTransient<DecodeViewModel>();
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

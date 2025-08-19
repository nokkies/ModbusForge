using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.Services;
using ModbusForge.ViewModels;
using System;
using System.IO;
using System.Windows;

namespace ModbusForge
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IConfiguration? Configuration { get; private set; }
        public static IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
        }
        +        private void Application_Startup(object sender, StartupEventArgs e)
+        {
+            // Create main window with MainViewModel from DI container
+            var mainWindow = new MainWindow(ServiceProvider.GetRequiredService<MainViewModel>());
+            mainWindow.Show();
+        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
            services.AddSingleton(Configuration);

            // Services
            services.AddSingleton<IModbusService, ModbusTcpService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
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

using System.Windows;

namespace ModbusForge.Services
{
    /// <summary>
    /// Production implementation that shuts down the WPF application.
    /// </summary>
    public class WpfApplicationLifetime : IApplicationLifetime
    {
        public void Shutdown()
        {
            Application.Current.Shutdown();
        }
    }
}

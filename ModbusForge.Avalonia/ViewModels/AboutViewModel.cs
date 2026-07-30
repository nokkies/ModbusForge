using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class AboutViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _version = string.Empty;

        public string Title { get; } = "About ModbusForge";
        public string Description { get; } = "A professional Modbus TCP/RTU/ASCII client and server built with .NET 8 and Avalonia.";
        public string Author { get; } = "Reinach van Nieuwenhuizen";
        public string LinkedInUrl { get; } = "https://www.linkedin.com/in/nokkies/";
        public string GitHubUrl { get; } = "https://github.com/nokkies/ModbusForge";
        public string Email { get; } = "reinach@softwareForge.cc";

        public AboutViewModel()
        {
            Version = GetVersion();
        }

        private static string GetVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var version = assembly?.GetName().Version;
                return version != null ? $"Version {version.Major}.{version.Minor}.{version.Build}" : "Version unknown";
            }
            catch (Exception)
            {
                return "Version unknown";
            }
        }
    }
}

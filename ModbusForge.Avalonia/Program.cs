using System;
using global::Avalonia;
using global::Avalonia.Fonts.Inter;

namespace ModbusForge.Avalonia
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static global::Avalonia.AppBuilder BuildAvaloniaApp()
            => global::Avalonia.AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont();
    }
}

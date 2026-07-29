using System;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Services
{
    /// <summary>
    /// Avalonia application lifetime adapter so view-models do not depend on Avalonia types.
    /// </summary>
    public sealed class AvaloniaApplicationLifetime : IApplicationLifetime
    {
        public void Shutdown()
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }
    }
}

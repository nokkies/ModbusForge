using System;

namespace ModbusForge.Services
{
    public interface IWindowService
    {
        void ShowPreferences();
        void ShowAbout();
        void ShowHelp(string? topic = null);
        void ShowKeyboardShortcuts();
        void ShowTroubleshooting();
    }
}

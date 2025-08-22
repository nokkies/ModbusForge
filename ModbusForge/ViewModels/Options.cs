namespace ModbusForge.ViewModels
{
    // NOTE:
    // These option provider classes are intentionally disabled because
    // equivalent string arrays are now declared in XAML resources
    // (see MainWindow.xaml: ModeOptionsAll, TypeOptionsAll, AreaOptionsAll).
    // Keeping these classes alongside the copies in MainViewModel.cs causes
    // duplicate type definitions (CS0101) and breaks the XAML designer.
    // If you prefer C# providers, remove the XAML arrays and re-enable below.

    #if false
    public static class ModeOptions
    {
        public static readonly string[] All = new[] { "Client", "Server" };
    }

    public static class TypeOptions
    {
        // Supported types for register display/editing
        public static readonly string[] All = new[] { "uint", "int", "real", "string" };
    }

    public static class AreaOptions
    {
        // Supported Modbus areas for Custom tab
        public static readonly string[] All = new[] { "HoldingRegister", "Coil", "InputRegister", "DiscreteInput" };
    }
    #endif
}

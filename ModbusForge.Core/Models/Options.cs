namespace ModbusForge.Models
{
    public static class TypeOptions
    {
        public static readonly string[] All = new[] { "uint", "int", "real", "string" };
    }

    public static class AreaOptions
    {
        // HoldingRegister and Coil are writable; InputRegister and DiscreteInput are read-only per Modbus spec
        public static readonly string[] All = new[] { "HoldingRegister", "Coil", "InputRegister", "DiscreteInput" };
    }

    public static class ModeOptions
    {
        public static readonly string[] All = new[] { "Client", "Server" };
    }
}

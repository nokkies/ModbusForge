namespace ModbusForge.Services
{
    /// <summary>
    /// Abstraction over application shutdown so that callers do not depend
    /// on <c>Application.Current</c> directly.
    /// </summary>
    public interface IApplicationLifetime
    {
        void Shutdown();
    }
}

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Persistent per-instance state for a function block.
    /// </summary>
    public interface IStateBag
    {
        T GetOrCreate<T>(string key) where T : new();
        T? Get<T>(string key);
        void Set<T>(string key, T value);
        bool TryGet<T>(string key, out T value);
    }
}

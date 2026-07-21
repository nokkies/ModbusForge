using System;
using System.Collections.Generic;

namespace ModbusForge.Simulation.Core
{
    /// <summary>
    /// Default implementation of per-instance function block state.
    /// </summary>
    public sealed class StateBag : IStateBag
    {
        private readonly Dictionary<string, object> _values = new();

        public T GetOrCreate<T>(string key) where T : new()
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }

            var created = new T();
            _values[key] = created;
            return created;
        }

        public T? Get<T>(string key)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }

            return default;
        }

        public void Set<T>(string key, T value)
        {
            _values[key] = value!;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }
    }
}

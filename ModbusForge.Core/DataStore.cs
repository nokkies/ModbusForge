using System;
using System.Collections;
using System.Collections.Generic;
using NModbus;
using NModbus.Data;

namespace ModbusForge.Data
{
    /// <summary>
    /// Compatibility data store that exposes the old NModbus4 1-based
    /// <see cref="ModbusDataCollection{T}"/> API on top of the NModbus v3
    /// <see cref="IPointSource{T}"/> implementation.
    /// </summary>
    public class DataStore
    {
        private readonly ModbusDataCollection<ushort> _holdingRegisters;
        private readonly ModbusDataCollection<ushort> _inputRegisters;
        private readonly ModbusDataCollection<bool> _coilDiscretes;
        private readonly ModbusDataCollection<bool> _inputDiscretes;

        public DataStore()
            : this(ushort.MaxValue)
        {
        }

        public DataStore(ushort size)
        {
            _holdingRegisters = new ModbusDataCollection<ushort>(size);
            _inputRegisters = new ModbusDataCollection<ushort>(size);
            _coilDiscretes = new ModbusDataCollection<bool>(size);
            _inputDiscretes = new ModbusDataCollection<bool>(size);
        }

        public ModbusDataCollection<ushort> HoldingRegisters => _holdingRegisters;
        public ModbusDataCollection<ushort> InputRegisters => _inputRegisters;
        public ModbusDataCollection<bool> CoilDiscretes => _coilDiscretes;
        public ModbusDataCollection<bool> InputDiscretes => _inputDiscretes;
    }

    /// <summary>
    /// Factory for creating <see cref="DataStore"/> instances with the same
    /// shape as the old NModbus4 <c>DataStoreFactory</c>.
    /// </summary>
    public static class DataStoreFactory
    {
        public static DataStore CreateDefaultDataStore()
            => new DataStore();
    }

    /// <summary>
    /// 1-based Modbus data collection backed by an NModbus v3 <see cref="IPointSource{T}"/>.
    /// Index 0 is an unused placeholder, matching the NModbus4 behaviour.
    /// </summary>
    public class ModbusDataCollection<T> : IList<T>, IReadOnlyList<T> where T : struct
    {
        private readonly IPointSource<T> _pointSource;
        private readonly int _count;

        public ModbusDataCollection()
            : this(ushort.MaxValue)
        {
        }

        public ModbusDataCollection(ushort size)
        {
            _pointSource = new PointSource<T>();
            _count = size + 1; // +1 for the unused 0-based placeholder at index 0
        }

        public int Count => _count;

        public bool IsReadOnly => false;

        public T this[int index]
        {
            get
            {
                if (index == 0)
                    return default;

                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index was out of range.");

                return _pointSource.ReadPoints((ushort)(index - 1), 1)[0];
            }
            set
            {
                if (index == 0)
                    throw new ArgumentOutOfRangeException(nameof(index), "0 is not a valid address for a Modbus data collection.");

                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index was out of range.");

                _pointSource.WritePoints((ushort)(index - 1), new[] { value });
            }
        }

        public T this[ushort index]
        {
            get => this[(int)index];
            set => this[(int)index] = value;
        }

        public void Add(T item)
        {
            // The collection is pre-sized to the full Modbus address space.
            // Additional Add calls are ignored to preserve compatibility with
            // code that populated the old NModbus4 collection.
        }

        public void Clear()
        {
            for (int i = 1; i < Count; i++)
            {
                _pointSource.WritePoints((ushort)(i - 1), new[] { default(T) });
            }
        }

        public bool Contains(T item)
        {
            return IndexOf(item) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));

            for (int i = 0; i < Count && arrayIndex + i < array.Length; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        public int IndexOf(T item)
        {
            var comparer = EqualityComparer<T>.Default;
            for (int i = 1; i < Count; i++)
            {
                if (comparer.Equals(this[i], item))
                    return i;
            }

            return -1;
        }

        public void Insert(int index, T item)
            => throw new NotSupportedException();

        public bool Remove(T item)
            => throw new NotSupportedException();

        public void RemoveAt(int index)
            => throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

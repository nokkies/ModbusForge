using System;
using System.Collections;
using System.Collections.Generic;

namespace ModbusForge.Data
{
    /// <summary>
    /// Compatibility data store that exposes the old NModbus4 1-based
    /// <see cref="ModbusDataCollection{T}"/> API.
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
    /// 1-based Modbus data collection. Index 0 is an unused placeholder, matching the
    /// NModbus4 behaviour. Every access takes a lock, as the NModbus v3 point source
    /// did, but without its per-call LINQ slice and array allocations: the PLC runtime
    /// reads registers and coils one at a time, thousands of times per scan.
    /// </summary>
    public class ModbusDataCollection<T> : IList<T>, IReadOnlyList<T> where T : struct
    {
        private readonly object _sync = new();
        private readonly int _count;

        // Point n-1 holds index n. Allocated on the first write, so an unused table
        // costs nothing; until then every point reads as zero.
        private T[]? _points;

        public ModbusDataCollection()
            : this(ushort.MaxValue)
        {
        }

        public ModbusDataCollection(ushort size)
        {
            _count = size + 1; // +1 for the unused 0-based placeholder at index 0
        }

        /// <summary>The storage, allocated when first needed. Call only while holding <see cref="_sync"/>.</summary>
        private T[] Points => _points ??= new T[_count - 1];

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

                lock (_sync)
                {
                    return _points is { } points ? points[index - 1] : default;
                }
            }
            set
            {
                if (index == 0)
                    throw new ArgumentOutOfRangeException(nameof(index), "0 is not a valid address for a Modbus data collection.");

                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index), "Index was out of range.");

                lock (_sync)
                {
                    Points[index - 1] = value;
                }
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
            lock (_sync)
            {
                if (_points != null) Array.Clear(_points);
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

        /// <summary>
        /// Reads <paramref name="count"/> consecutive points starting at the 1-based
        /// <paramref name="index"/> in one call (the indexer costs a call per point).
        /// </summary>
        public T[] ReadRange(int index, int count)
        {
            if (index < 1 || count < 0 || index + count > Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Range was out of the collection.");

            if (count == 0) return Array.Empty<T>();

            var values = new T[count];
            ReadRange(index, values);
            return values;
        }

        /// <summary>Copies consecutive points starting at the 1-based <paramref name="index"/> into <paramref name="destination"/>.</summary>
        public void ReadRange(int index, Span<T> destination)
        {
            if (index < 1 || index + destination.Length > Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Range was out of the collection.");

            lock (_sync)
            {
                if (_points is { } points)
                    points.AsSpan(index - 1, destination.Length).CopyTo(destination);
                else
                    destination.Clear();
            }
        }

        /// <summary>Writes consecutive points starting at the 1-based <paramref name="index"/> in one call.</summary>
        public void WriteRange(int index, T[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            WriteRange(index, (ReadOnlySpan<T>)values);
        }

        /// <summary>Writes consecutive points starting at the 1-based <paramref name="index"/> in one call.</summary>
        public void WriteRange(int index, ReadOnlySpan<T> values)
        {
            if (index < 1 || index + values.Length > Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Range was out of the collection.");

            if (values.Length == 0) return;

            lock (_sync)
            {
                values.CopyTo(Points.AsSpan(index - 1));
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

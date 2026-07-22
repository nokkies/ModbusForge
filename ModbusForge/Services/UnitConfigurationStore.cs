using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// In-memory store for Unit ID configurations, available Unit IDs, and selection state.
    /// </summary>
    public class UnitConfigurationStore : IUnitConfigurationStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<byte, UnitIdConfiguration> _configurations = new();
        private readonly IDispatcher? _dispatcher;
        private byte _selectedUnitId = 1;

        public UnitConfigurationStore(IDispatcher? dispatcher = null)
        {
            _dispatcher = dispatcher;
            AvailableUnitIds = new ObservableCollection<byte>();
            AvailableUnitIds.CollectionChanged += OnAvailableUnitIdsCollectionChanged;
        }

        public IReadOnlyDictionary<byte, UnitIdConfiguration> UnitConfigurations
        {
            get
            {
                lock (_sync)
                {
                    return _configurations;
                }
            }
        }

        public ObservableCollection<byte> AvailableUnitIds { get; }

        public byte SelectedUnitId
        {
            get
            {
                lock (_sync)
                {
                    return _selectedUnitId;
                }
            }
            set
            {
                lock (_sync)
                {
                    if (_selectedUnitId == value)
                    {
                        return;
                    }

                    _selectedUnitId = value;
                }

                SelectedUnitIdChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public UnitIdConfiguration CurrentConfig => GetOrCreateConfiguration(SelectedUnitId);

        public event EventHandler? SelectedUnitIdChanged;
        public event EventHandler? AvailableUnitIdsChanged;

        public UnitIdConfiguration GetOrCreateConfiguration(byte unitId)
        {
            lock (_sync)
            {
                if (!_configurations.TryGetValue(unitId, out var configuration))
                {
                    configuration = new UnitIdConfiguration(unitId);
                    _configurations[unitId] = configuration;
                }

                return configuration;
            }
        }

        public void SetConfiguration(byte unitId, UnitIdConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            lock (_sync)
            {
                _configurations[unitId] = configuration.Clone();
            }
        }

        public bool TryGetConfiguration(byte unitId, out UnitIdConfiguration configuration)
        {
            lock (_sync)
            {
                return _configurations.TryGetValue(unitId, out configuration!);
            }
        }

        public void MergeConfigurations(IDictionary<byte, UnitIdConfiguration> configurations)
        {
            if (configurations is null)
            {
                throw new ArgumentNullException(nameof(configurations));
            }

            lock (_sync)
            {
                foreach (var kvp in configurations)
                {
                    if (!_configurations.ContainsKey(kvp.Key))
                    {
                        _configurations[kvp.Key] = kvp.Value.Clone();
                    }
                }
            }
        }

        public void PopulateAvailableUnitIds(IEnumerable<byte> unitIds)
        {
            var ids = (unitIds ?? Enumerable.Empty<byte>()).OrderBy(x => x).ToList();

            InvokeOnDispatcher(() =>
            {
                AvailableUnitIds.Clear();
                foreach (var id in ids)
                {
                    AvailableUnitIds.Add(id);
                }

                if (!AvailableUnitIds.Contains(SelectedUnitId) && AvailableUnitIds.Count > 0)
                {
                    SelectedUnitId = AvailableUnitIds[0];
                }
            });
        }

        public UnitIdConfiguration CloneConfiguration(byte unitId)
        {
            lock (_sync)
            {
                if (_configurations.TryGetValue(unitId, out var configuration))
                {
                    return configuration.Clone();
                }
            }

            return new UnitIdConfiguration(unitId);
        }

        public bool RemoveConfiguration(byte unitId)
        {
            lock (_sync)
            {
                return _configurations.Remove(unitId);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _configurations.Clear();
                _selectedUnitId = 1;
            }
        }

        private void InvokeOnDispatcher(Action action)
        {
            if (_dispatcher is not null)
            {
                _dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        private void OnAvailableUnitIdsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            AvailableUnitIdsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

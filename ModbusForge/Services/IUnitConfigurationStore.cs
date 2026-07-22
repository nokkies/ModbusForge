using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ModbusForge.Models;

namespace ModbusForge.Services
{
    /// <summary>
    /// Manages the collection of Unit ID configurations, the available Unit IDs,
    /// and the currently selected Unit ID for the application.
    /// </summary>
    public interface IUnitConfigurationStore
    {
        IReadOnlyDictionary<byte, UnitIdConfiguration> UnitConfigurations { get; }
        ObservableCollection<byte> AvailableUnitIds { get; }
        byte SelectedUnitId { get; set; }
        UnitIdConfiguration CurrentConfig { get; }

        event EventHandler? SelectedUnitIdChanged;
        event EventHandler? AvailableUnitIdsChanged;

        UnitIdConfiguration GetOrCreateConfiguration(byte unitId);
        void SetConfiguration(byte unitId, UnitIdConfiguration configuration);
        bool TryGetConfiguration(byte unitId, out UnitIdConfiguration configuration);
        void MergeConfigurations(IDictionary<byte, UnitIdConfiguration> configurations);
        void PopulateAvailableUnitIds(IEnumerable<byte> unitIds);
        UnitIdConfiguration CloneConfiguration(byte unitId);
        bool RemoveConfiguration(byte unitId);
        void Clear();
    }
}

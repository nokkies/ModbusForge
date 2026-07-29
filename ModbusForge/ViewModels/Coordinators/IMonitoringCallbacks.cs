using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModbusForge.Models;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Callback surface used by <see cref="MonitoringCoordinator"/> to read
    /// monitoring state from and execute monitoring operations on the view model.
    /// </summary>
    public interface IMonitoringCallbacks
    {
        bool IsConnected { get; }
        bool IsServerMode { get; }
        byte UnitId { get; }
        byte EffectiveUnitId { get; }

        bool GlobalMonitorEnabled { get; }

        bool HoldingMonitorEnabled { get; }
        int HoldingMonitorPeriodMs { get; }
        int HoldingStartAddress { get; }
        int HoldingCount { get; }

        bool InputRegistersMonitorEnabled { get; }
        int InputRegistersMonitorPeriodMs { get; }
        int InputRegisterStartAddress { get; }
        int InputRegisterCount { get; }

        bool CoilsMonitorEnabled { get; }
        int CoilsMonitorPeriodMs { get; }
        int CoilStartAddress { get; }
        int CoilCount { get; }

        bool DiscreteInputsMonitorEnabled { get; }
        int DiscreteInputsMonitorPeriodMs { get; }
        int DiscreteInputStartAddress { get; }
        int DiscreteInputCount { get; }

        DateTime LastHoldingReadUtc { get; set; }
        DateTime LastInputRegReadUtc { get; set; }
        DateTime LastCoilsReadUtc { get; set; }
        DateTime LastDiscreteReadUtc { get; set; }

        IEnumerable<CustomEntry> GetCustomEntriesSnapshot();

        Task ReadRegistersAsync();
        Task ReadInputRegistersAsync();
        Task ReadCoilsAsync();
        Task ReadDiscreteInputsAsync();

        Task WriteCustomNowAsync(CustomEntry entry);
        Task ProcessTrendSamplingAsync();
        Task HeartbeatAsync();

        void ApplyPollingResult(PollingResult result);
        void SetStatusMessage(string message);
        void SetHasConnectionError(bool hasError);

        bool HasConnectionError { get; set; }
        DateTime LastErrorTime { get; set; }

        bool CustomReadMonitorEnabled { get; }

        Task ReadCustomNowAsync(CustomEntry entry);
    }
}

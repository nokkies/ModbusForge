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

        bool GlobalMonitorEnabled { get; }

        bool HoldingMonitorEnabled { get; }
        int HoldingMonitorPeriodMs { get; }

        bool InputRegistersMonitorEnabled { get; }
        int InputRegistersMonitorPeriodMs { get; }

        bool CoilsMonitorEnabled { get; }
        int CoilsMonitorPeriodMs { get; }

        bool DiscreteInputsMonitorEnabled { get; }
        int DiscreteInputsMonitorPeriodMs { get; }

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

        bool HasConnectionError { get; set; }
        DateTime LastErrorTime { get; set; }
    }
}

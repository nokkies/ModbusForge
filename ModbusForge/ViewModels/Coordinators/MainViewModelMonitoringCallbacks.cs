using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModbusForge.Models;
using ModbusForge.ViewModels;

namespace ModbusForge.ViewModels.Coordinators
{
    /// <summary>
    /// Forwarding implementation of <see cref="IMonitoringCallbacks"/> that resolves the
    /// <see cref="MainViewModel"/> lazily so the coordinator can be constructed before
    /// the view model without creating a circular DI dependency.
    /// </summary>
    internal sealed class MainViewModelMonitoringCallbacks : IMonitoringCallbacks
    {
        private readonly IServiceProvider _serviceProvider;
        private MainViewModel? _viewModel;

        public MainViewModelMonitoringCallbacks(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        private MainViewModel ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<MainViewModel>();

        public bool IsConnected => ViewModel.IsConnected;
        public bool IsServerMode => ViewModel.IsServerMode;
        public byte UnitId => ViewModel.UnitId;

        public bool GlobalMonitorEnabled => ViewModel.GlobalMonitorEnabled;

        public bool HoldingMonitorEnabled => ViewModel.HoldingMonitorEnabled;
        public int HoldingMonitorPeriodMs => ViewModel.HoldingMonitorPeriodMs;

        public bool InputRegistersMonitorEnabled => ViewModel.InputRegistersMonitorEnabled;
        public int InputRegistersMonitorPeriodMs => ViewModel.InputRegistersMonitorPeriodMs;

        public bool CoilsMonitorEnabled => ViewModel.CoilsMonitorEnabled;
        public int CoilsMonitorPeriodMs => ViewModel.CoilsMonitorPeriodMs;

        public bool DiscreteInputsMonitorEnabled => ViewModel.DiscreteInputsMonitorEnabled;
        public int DiscreteInputsMonitorPeriodMs => ViewModel.DiscreteInputsMonitorPeriodMs;

        public DateTime LastHoldingReadUtc
        {
            get => ViewModel.LastHoldingReadUtc;
            set => ViewModel.LastHoldingReadUtc = value;
        }

        public DateTime LastInputRegReadUtc
        {
            get => ViewModel.LastInputRegReadUtc;
            set => ViewModel.LastInputRegReadUtc = value;
        }

        public DateTime LastCoilsReadUtc
        {
            get => ViewModel.LastCoilsReadUtc;
            set => ViewModel.LastCoilsReadUtc = value;
        }

        public DateTime LastDiscreteReadUtc
        {
            get => ViewModel.LastDiscreteReadUtc;
            set => ViewModel.LastDiscreteReadUtc = value;
        }

        public IEnumerable<CustomEntry> GetCustomEntriesSnapshot() => ViewModel.GetCustomEntriesSnapshot();

        public Task ReadRegistersAsync() => ViewModel.ReadRegistersAsync();
        public Task ReadInputRegistersAsync() => ViewModel.ReadInputRegistersAsync();
        public Task ReadCoilsAsync() => ViewModel.ReadCoilsAsync();
        public Task ReadDiscreteInputsAsync() => ViewModel.ReadDiscreteInputsAsync();

        public Task WriteCustomNowAsync(CustomEntry entry) => ViewModel.WriteCustomNowAsync(entry);
        public Task ProcessTrendSamplingAsync() => ViewModel.ProcessTrendSamplingAsync();
        public Task HeartbeatAsync() => ViewModel.HeartbeatAsync();

        public bool HasConnectionError
        {
            get => ViewModel.HasConnectionError;
            set => ViewModel.HasConnectionError = value;
        }

        public DateTime LastErrorTime
        {
            get => ViewModel.LastErrorTime;
            set => ViewModel.LastErrorTime = value;
        }
    }
}

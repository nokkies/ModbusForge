using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class SignalGeneratorViewModelTests
    {
        [Fact]
        public void XAxisLabeler_NeverThrows_ForDegenerateChartCoordinates()
        {
            // The preview chart shares the LiveCharts labeler contract with the
            // trend chart: the chart can pass NaN/±infinity/out-of-range
            // coordinates for degenerate axis domains, and the labeler must
            // render (even empty) rather than throw.
            var vm = new SignalGeneratorViewModel(new FakeConnectionManager(), new SyncDispatcher());
            var labeler = vm.XAxes[0].Labeler;
            Assert.NotNull(labeler);

            Assert.Equal(string.Empty, labeler(double.NaN));
            Assert.Equal(string.Empty, labeler(double.PositiveInfinity));
            Assert.Equal(string.Empty, labeler(double.NegativeInfinity));
            Assert.Equal(string.Empty, labeler(double.MaxValue));
            Assert.Equal(string.Empty, labeler(double.MinValue));
            Assert.Equal(string.Empty, labeler(-1.0));          // before 0001-01-01
            Assert.Equal(string.Empty, labeler(1.0e19));        // after 9999-12-31

            // Axis coordinates are DateTime ticks (DateTimePoint's X).
            var known = DateTime.Parse("2026-08-17T12:34:56");
            Assert.Equal("12:34:56", labeler(known.Ticks));
        }

        private sealed class FakeConnectionManager : IConnectionManager
        {
            public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
            public ConnectionProfile? ActiveProfile => null;
            public IModbusService? ActiveService => null;

            public event EventHandler<ConnectionProfile?>? ActiveProfileChanged { add { } remove { } }
            public event EventHandler<ConnectionProfile>? ProfileConnected { add { } remove { } }
            public event EventHandler<ConnectionProfile>? ProfileDisconnected { add { } remove { } }

            public void AddProfile(ConnectionProfile profile) => Profiles.Add(profile);
            public void RemoveProfile(ConnectionProfile profile) => Profiles.Remove(profile);
            public void SetActiveProfile(ConnectionProfile profile) { }
            public Task<bool> ConnectProfileAsync(ConnectionProfile profile) => Task.FromResult(false);
            public Task DisconnectProfileAsync(ConnectionProfile profile) => Task.CompletedTask;
            public Task DisconnectAllAsync() => Task.CompletedTask;
            public IModbusService? GetServiceForProfile(ConnectionProfile profile) => null;
            public void SaveProfiles() { }
            public void LoadProfiles() { }
        }
    }
}

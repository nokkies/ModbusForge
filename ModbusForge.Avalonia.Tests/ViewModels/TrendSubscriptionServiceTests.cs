using ModbusForge.Avalonia.Services;
using ModbusForge.Avalonia.ViewModels;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    public class TrendSubscriptionServiceTests
    {
        private static (TrendSubscriptionService service, UnitConfigurationStore store) CreateService()
        {
            var store = new UnitConfigurationStore();
            return (new TrendSubscriptionService(store), store);
        }

        [Fact]
        public void AddPen_CreatesWatchEntry_WithTrendAndMonitoringEnabled()
        {
            var (service, store) = CreateService();

            var key = service.AddPen("HoldingRegister", 5, "HR 5", 1000);

            var entry = Assert.Single(store.CurrentConfig.CustomEntries);
            Assert.Equal(key, entry.Name);
            Assert.Equal(5, entry.Address);
            Assert.Equal("HoldingRegister", entry.Area);
            Assert.True(entry.Trend);
            Assert.True(entry.Monitor);
            Assert.Equal(1000, entry.ReadPeriodMs);
        }

        [Fact]
        public void AddPen_UsesDefaultName_WhenNoneRequested()
        {
            var (service, store) = CreateService();

            var key = service.AddPen("HoldingRegister", 5, null, 1000);

            Assert.Equal("HR Trend 5", key);
            var entry = Assert.Single(store.CurrentConfig.CustomEntries);
            Assert.Equal("HR Trend 5", entry.Name);
        }

        [Fact]
        public void AddPen_ReusesExistingEntry_KeepingItsNameAsTheStableKey()
        {
            var (service, store) = CreateService();
            store.CurrentConfig.CustomEntries.Add(new ModbusForge.Models.CustomEntry
            {
                Name = "My custom watch",
                Address = 5,
                Area = "HoldingRegister",
                Type = "real",
                Value = "1.5",
                WriteValue = "1.5",
                Monitor = false,
                Trend = false
            });

            var key = service.AddPen("HoldingRegister", 5, "HR 5", 500);

            Assert.Equal("My custom watch", key);
            var entry = Assert.Single(store.CurrentConfig.CustomEntries);
            Assert.True(entry.Trend);
            Assert.True(entry.Monitor);
            Assert.Equal(500, entry.ReadPeriodMs);
            // Existing configuration is untouched.
            Assert.Equal("real", entry.Type);
            Assert.Equal("1.5", entry.Value);
        }

        [Fact]
        public void RemovePen_ClearsTrendButKeepsTheWatchEntry()
        {
            var (service, store) = CreateService();
            service.AddPen("Coil", 3, "Coil Trend 3", 1000);
            var entry = Assert.Single(store.CurrentConfig.CustomEntries);
            Assert.True(entry.Trend);

            Assert.True(service.RemovePen("Coil Trend 3"));

            // The entry survives (it is still a watch item in Custom Watch)
            // but no longer feeds the trend, so the pen stays removed.
            var kept = Assert.Single(store.CurrentConfig.CustomEntries);
            Assert.Equal("Coil Trend 3", kept.Name);
            Assert.False(kept.Trend);
        }

        [Fact]
        public void RemovePen_UnknownKey_ReturnsFalse()
        {
            var (service, _) = CreateService();

            Assert.False(service.RemovePen("Imported:something"));
        }

        [Fact]
        public void DefaultName_MatchesHistoricalContextMenuNames()
        {
            var (service, _) = CreateService();

            Assert.Equal("HR Trend 1", service.DefaultName("HoldingRegister", 1));
            Assert.Equal("IR Trend 2", service.DefaultName("InputRegister", 2));
            Assert.Equal("Coil Trend 3", service.DefaultName("Coil", 3));
            Assert.Equal("DiscreteInput Trend 4", service.DefaultName("DiscreteInput", 4));
        }

        [Fact]
        public void AddPen_InvalidInputs_Throw()
        {
            var (service, _) = CreateService();

            Assert.Throws<System.ArgumentException>(() => service.AddPen("", 1, null, 1000));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => service.AddPen("Coil", -1, null, 1000));
        }
    }
}

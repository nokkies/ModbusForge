using ModbusForge.Avalonia.Services;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Avalonia.Tests.ViewModels
{
    /// <summary>
    /// The subscription service now manages first-class trend pens on the
    /// unit configuration - it must never create or mutate custom watch
    /// entries.
    /// </summary>
    public class TrendSubscriptionServiceTests
    {
        private static (TrendSubscriptionService service, UnitConfigurationStore store) CreateService()
        {
            var store = new UnitConfigurationStore();
            return (new TrendSubscriptionService(store), store);
        }

        [Fact]
        public void AddPen_CreatesPen_WithRequestedDetails()
        {
            var (service, store) = CreateService();

            var added = service.AddPen("HoldingRegister", 5, "HR 5", 1000, "real");

            var pen = Assert.Single(store.CurrentConfig.TrendPens);
            Assert.Same(added, pen);
            // The key is born with the unique name and stays stable while
            // the display name is renamable.
            Assert.Equal("HR 5", pen.Key);
            Assert.Equal("HR 5", pen.Name);
            Assert.Equal(5, pen.Address);
            Assert.Equal("HoldingRegister", pen.Area);
            Assert.Equal("real", pen.Type);
            Assert.Equal(1000, pen.ReadPeriodMs);

            // Full decoupling: no watch entry is created or touched.
            Assert.Empty(store.CurrentConfig.CustomEntries);
        }

        [Fact]
        public void AddPen_Defaults_TypeToInt_WhenNoneGiven()
        {
            var (service, store) = CreateService();

            service.AddPen("HoldingRegister", 5, "HR 5", 1000);

            Assert.Equal("int", Assert.Single(store.CurrentConfig.TrendPens).Type);
        }

        [Fact]
        public void AddPen_UsesDefaultName_WhenNoneRequested()
        {
            var (service, store) = CreateService();

            var added = service.AddPen("HoldingRegister", 5, null, 1000);

            Assert.Equal("HR Trend 5", added.Key);
            Assert.Equal("HR Trend 5", Assert.Single(store.CurrentConfig.TrendPens).Name);
        }

        [Fact]
        public void AddPen_ReusesExistingPen_KeepingItsStableKey()
        {
            var (service, store) = CreateService();
            store.CurrentConfig.TrendPens.Add(new TrendPen
            {
                Key = "Speed",
                Name = "Speed",
                Area = "HoldingRegister",
                Address = 5,
                Type = "real",
                ReadPeriodMs = 250
            });

            var reused = service.AddPen("HoldingRegister", 5, "HR 5", 500);

            Assert.Equal("Speed", reused.Key);
            var pen = Assert.Single(store.CurrentConfig.TrendPens);
            Assert.Equal("real", pen.Type);
            Assert.Equal(500, pen.ReadPeriodMs);
        }

        [Fact]
        public void AddPen_NameCollision_IsMadeUniqueWithinTheUnit()
        {
            var (service, store) = CreateService();
            store.CurrentConfig.TrendPens.Add(new TrendPen
            {
                Key = "Speed",
                Name = "Speed",
                Area = "InputRegister",
                Address = 1
            });

            var added = service.AddPen("HoldingRegister", 2, "Speed", 1000);

            Assert.Equal("Speed 2", added.Key);
            Assert.Equal("Speed 2", added.Name);
            Assert.Equal(2, store.CurrentConfig.TrendPens.Count);
        }

        [Fact]
        public void RemovePen_RemovesThePen_FromTheUnit()
        {
            var (service, store) = CreateService();
            service.AddPen("Coil", 3, "Coil Trend 3", 1000);
            Assert.Single(store.CurrentConfig.TrendPens);

            Assert.True(service.RemovePen("Coil Trend 3"));

            Assert.Empty(store.CurrentConfig.TrendPens);
            Assert.Empty(store.CurrentConfig.CustomEntries);
        }

        [Fact]
        public void RemovePen_UnknownKey_ReturnsFalse()
        {
            var (service, _) = CreateService();

            Assert.False(service.RemovePen("Imported:something"));
        }

        [Fact]
        public void RenamePen_ChangesName_KeepsTheStableKey()
        {
            var (service, store) = CreateService();
            var added = service.AddPen("HoldingRegister", 5, "HR 5", 1000);

            Assert.True(service.RenamePen(added.Key, "Pressure"));

            var pen = Assert.Single(store.CurrentConfig.TrendPens);
            Assert.Equal("Pressure", pen.Name);
            Assert.Equal("HR 5", pen.Key);
        }

        [Fact]
        public void RenamePen_NameAlreadyUsed_ReturnsFalse_AndKeepsOldName()
        {
            var (service, store) = CreateService();
            var first = service.AddPen("HoldingRegister", 1, "Speed", 1000);
            var second = service.AddPen("HoldingRegister", 2, "Flow", 1000);

            Assert.False(service.RenamePen(second.Key, "Speed"));

            Assert.Equal("Flow", store.CurrentConfig.TrendPens.Single(p => p.Key == second.Key).Name);
            Assert.Equal("Speed", store.CurrentConfig.TrendPens.Single(p => p.Key == first.Key).Name);
        }

        [Fact]
        public void RenamePen_BlankName_ReturnsFalse()
        {
            var (service, store) = CreateService();
            var added = service.AddPen("HoldingRegister", 1, "Speed", 1000);

            Assert.False(service.RenamePen(added.Key, "   "));
            Assert.False(service.RenamePen(added.Key, null));
            Assert.False(service.RenamePen("unknown", "Speed"));

            Assert.Equal("Speed", Assert.Single(store.CurrentConfig.TrendPens).Name);
        }

        [Fact]
        public void RemovePen_AfterRename_StillFindsThePenByItsOriginalKey()
        {
            var (service, store) = CreateService();
            var added = service.AddPen("Coil", 3, "Coil Trend 3", 1000);
            Assert.True(service.RenamePen(added.Key, "Run/Stop"));

            Assert.True(service.RemovePen(added.Key));

            Assert.Empty(store.CurrentConfig.TrendPens);
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

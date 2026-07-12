using System.Collections.Generic;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.Services;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class UnitConfigurationStoreTests
    {
        private static UnitConfigurationStore CreateStore() => new UnitConfigurationStore(new ImmediateDispatcher());

        [Fact]
        public void GetOrCreateConfiguration_CreatesNewConfig_WhenMissing()
        {
            // Arrange
            var store = CreateStore();

            // Act
            var config = store.GetOrCreateConfiguration(5);

            // Assert
            Assert.NotNull(config);
            Assert.Equal(5, config.UnitId);
            Assert.Single(store.UnitConfigurations);
            Assert.Same(config, store.UnitConfigurations[5]);
        }

        [Fact]
        public void CurrentConfig_FollowsSelectedUnitId()
        {
            // Arrange
            var store = CreateStore();
            store.SelectedUnitId = 3;

            // Act
            var config = store.CurrentConfig;

            // Assert
            Assert.NotNull(config);
            Assert.Equal(3, config.UnitId);
            Assert.Same(config, store.GetOrCreateConfiguration(3));
        }

        [Fact]
        public void MergeConfigurations_AddsOnlyMissingKeys()
        {
            // Arrange
            var store = CreateStore();
            var existing = store.GetOrCreateConfiguration(1);
            existing.RegisterSettings.RegisterCount = 42;

            var incoming = new Dictionary<byte, UnitIdConfiguration>
            {
                [1] = new UnitIdConfiguration(1) { RegisterSettings = { RegisterCount = 99 } },
                [2] = new UnitIdConfiguration(2) { RegisterSettings = { RegisterCount = 77 } }
            };

            // Act
            store.MergeConfigurations(incoming);

            // Assert
            Assert.Equal(2, store.UnitConfigurations.Count);
            Assert.Equal(42, store.UnitConfigurations[1].RegisterSettings.RegisterCount);
            Assert.Equal(77, store.UnitConfigurations[2].RegisterSettings.RegisterCount);
        }

        [Fact]
        public void PopulateAvailableUnitIds_SortsAndUpdatesSelection()
        {
            // Arrange
            var store = CreateStore();
            store.SelectedUnitId = 10;

            // Act
            store.PopulateAvailableUnitIds(new[] { (byte)5, (byte)2, (byte)7 });

            // Assert
            Assert.Equal(new[] { (byte)2, (byte)5, (byte)7 }, store.AvailableUnitIds.ToList());
            Assert.Equal(2, store.SelectedUnitId);
        }

        [Fact]
        public void PopulateAvailableUnitIds_KeepsSelection_WhenPresent()
        {
            // Arrange
            var store = CreateStore();
            store.SelectedUnitId = 5;

            // Act
            store.PopulateAvailableUnitIds(new[] { (byte)5, (byte)2, (byte)7 });

            // Assert
            Assert.Equal(new[] { (byte)2, (byte)5, (byte)7 }, store.AvailableUnitIds.ToList());
            Assert.Equal(5, store.SelectedUnitId);
        }

        [Fact]
        public void CloneConfiguration_ReturnsIndependentCopy()
        {
            // Arrange
            var store = CreateStore();
            var original = store.GetOrCreateConfiguration(4);
            original.RegisterSettings.RegisterCount = 123;

            // Act
            var clone = store.CloneConfiguration(4);

            // Assert
            Assert.Equal(4, clone.UnitId);
            Assert.Equal(123, clone.RegisterSettings.RegisterCount);
            Assert.NotSame(original, clone);

            clone.RegisterSettings.RegisterCount = 456;
            Assert.Equal(123, store.UnitConfigurations[4].RegisterSettings.RegisterCount);
        }

        [Fact]
        public void SetConfiguration_StoresCloneAndIsIndependent()
        {
            // Arrange
            var store = CreateStore();
            var config = new UnitIdConfiguration(6) { RegisterSettings = { RegisterCount = 50 } };

            // Act
            store.SetConfiguration(6, config);
            config.RegisterSettings.RegisterCount = 99;

            // Assert
            Assert.Equal(50, store.UnitConfigurations[6].RegisterSettings.RegisterCount);
        }

        [Fact]
        public void RemoveConfiguration_RemovesSpecifiedConfig()
        {
            // Arrange
            var store = CreateStore();
            store.GetOrCreateConfiguration(8);

            // Act
            var removed = store.RemoveConfiguration(8);

            // Assert
            Assert.True(removed);
            Assert.Empty(store.UnitConfigurations);
        }

        [Fact]
        public void Clear_RemovesAllConfigurations()
        {
            // Arrange
            var store = CreateStore();
            store.GetOrCreateConfiguration(2);
            store.GetOrCreateConfiguration(3);
            store.SelectedUnitId = 3;

            // Act
            store.Clear();

            // Assert
            Assert.Empty(store.UnitConfigurations);
            Assert.Equal(1, store.SelectedUnitId);
        }
    }
}

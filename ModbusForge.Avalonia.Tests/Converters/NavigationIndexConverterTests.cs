using System;
using System.Linq;
using ModbusForge.Avalonia.Converters;
using Xunit;

namespace ModbusForge.Avalonia.Tests.Converters
{
    public class NavigationIndexConverterTests
    {
        private static readonly NavigationIndexConverter Converter = new();

        [Fact]
        public void NavigationAndTabIndices_AreExactInverses()
        {
            // The converter holds two hard-coded maps; a drift between them
            // (e.g. when a tab is inserted) would silently break navigation.
            var navToTab = Converter.GetType().GetField("NavigationToTab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null) as int[] ?? throw new InvalidOperationException("NavigationToTab missing");
            var tabToNav = Converter.GetType().GetField("TabToNavigation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null) as int[] ?? throw new InvalidOperationException("TabToNavigation missing");

            Assert.Equal(16, navToTab.Length);
            Assert.Equal(16, tabToNav.Length);

            for (var nav = 0; nav < navToTab.Length; nav++)
            {
                var tab = navToTab[nav];
                Assert.True(tab >= 0 && tab < tabToNav.Length, $"Navigation {nav} maps to out-of-range tab {tab}");
                Assert.Equal(nav, tabToNav[tab]);
            }

            // Every tab index must be reachable from exactly one navigation entry.
            Assert.Equal(16, navToTab.Distinct().Count());
        }

        [Fact]
        public void NewRulesTab_SitsAfterScriptEditor()
        {
            // Regression guard for the tab inserted in round 9: the navigation
            // "Rules" entry (index 5) must open the Rules tab (index 5).
            Assert.Equal(5, Converter.ConvertBack(5, typeof(int), null, null!));

            // And the tab-control side maps it back.
            Assert.Equal(5, Converter.Convert(5, typeof(int), null, null!));
        }

        [Theory]
        [InlineData(1, 1)]   // Trends unchanged
        [InlineData(2, 2)]   // Frame Inspector unchanged
        [InlineData(4, 4)]   // Script Editor unchanged
        [InlineData(15, 15)] // Debug shifted to the new last slot
        public void ConvertBack_MapsNavigationToTab(int nav, int expectedTab)
        {
            Assert.Equal(expectedTab, Converter.ConvertBack(nav, typeof(int), null, null!));
        }
    }
}

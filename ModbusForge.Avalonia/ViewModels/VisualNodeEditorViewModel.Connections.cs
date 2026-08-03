using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Models;

namespace ModbusForge.Avalonia.ViewModels
{
    public partial class VisualNodeEditorViewModel
    {
        /// <summary>
        /// The diameter of a node connector port in logical canvas units.
        /// </summary>
        private const double PortDiameter = 10.0;

        /// <summary>
        /// Half the diameter of a connector port; used to vertically offset
        /// stacked input ports around the node center.
        /// </summary>
        private const double PortVerticalOffset = PortDiameter / 2.0;

        [ObservableProperty]
        private bool _showConnectors = true;

        /// <summary>
        /// Public entry point for refreshing the rendered connection lines.
        /// </summary>
        public void UpdateConnectionLines() => RefreshConnectionLines();

        /// <summary>
        /// Selects the connection with the specified id and clears the node selection.
        /// </summary>
        public void SelectConnection(string? connectionId)
        {
            if (connectionId == null)
            {
                SelectedConnection = null;
                return;
            }

            SelectedConnection = Config.Connections.FirstOrDefault(c => c.Id == connectionId);
        }

        partial void OnShowConnectorsChanged(bool value) => UpdateConnectionLines();
    }
}

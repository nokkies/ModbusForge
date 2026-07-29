using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Services;

namespace ModbusForge.ViewModels
{
    /// <summary>
    /// View model for the byte-level Modbus frame inspector.
    /// </summary>
    public partial class FrameInspectorViewModel : ObservableObject
    {
        private readonly ModbusFrameLogger _logger;

        [ObservableProperty]
        private string _title = "Frame Inspector";

        public FrameInspectorViewModel(ModbusFrameLogger logger, string? title = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (!string.IsNullOrWhiteSpace(title))
                Title = title;
            ClearCommand = new RelayCommand(() => _logger.Clear());
        }

        /// <summary>
        /// The captured frames.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<Models.ModbusFrameLog> Frames => _logger.Frames;

        public ICommand ClearCommand { get; }
    }
}

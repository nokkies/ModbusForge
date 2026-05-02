using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Data types supported by tags
    /// </summary>
    public enum TagDataType
    {
        Bool,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Float,
        Double,
        String
    }

    /// <summary>
    /// Represents a symbolic tag with Modbus address mapping
    /// </summary>
    public partial class Tag : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        private string _group = "Default";  // For hierarchical organization

        [ObservableProperty]
        private PlcArea _area = PlcArea.HoldingRegister;

        [ObservableProperty]
        private int _address = 1;

        [ObservableProperty]
        private TagDataType _dataType = TagDataType.UInt16;

        [ObservableProperty]
        private double _scale = 1.0;  // For analog scaling

        [ObservableProperty]
        private double _offset = 0.0;  // For analog offset

        [ObservableProperty]
        private string _units = "";

        [ObservableProperty]
        private object? _currentValue;

        [ObservableProperty]
        private DateTime _lastUpdated = DateTime.MinValue;

        [ObservableProperty]
        private bool _isAlarmEnabled;

        [ObservableProperty]
        private double? _alarmHigh;

        [ObservableProperty]
        private double? _alarmLow;

        [ObservableProperty]
        private bool _isReadOnly;

        /// <summary>
        /// Full address string (e.g., "HoldingRegister:1" or "HR1")
        /// </summary>
        public string FullAddress => $"{Area}:{Address}";

        /// <summary>
        /// Display name including group
        /// </summary>
        public string DisplayName => $"{Group}.{Name}";

        /// <summary>
        /// Scaled value (applies scale and offset)
        /// </summary>
        public double? ScaledValue
        {
            get
            {
                if (CurrentValue == null) return null;
                
                try
                {
                    var raw = Convert.ToDouble(CurrentValue);
                    return (raw * Scale) + Offset;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Formatted value with units
        /// </summary>
        public string FormattedValue
        {
            get
            {
                if (CurrentValue == null) return "---";
                
                var scaled = ScaledValue;
                if (scaled.HasValue && !string.IsNullOrEmpty(Units))
                {
                    return $"{scaled.Value:F2} {Units}";
                }
                
                return CurrentValue.ToString() ?? "---";
            }
        }
    }

    /// <summary>
    /// Group of tags for hierarchical organization
    /// </summary>
    public partial class TagGroup : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        private string _parentGroup = "";  // Empty = root level

        [ObservableProperty]
        private ObservableCollection<Tag> _tags = new();

        [ObservableProperty]
        private ObservableCollection<TagGroup> _subGroups = new();

        /// <summary>
        /// Full path including parent groups
        /// </summary>
        public string FullPath
        {
            get
            {
                if (string.IsNullOrEmpty(ParentGroup))
                    return Name;
                return $"{ParentGroup}.{Name}";
            }
        }

        /// <summary>
        /// Total tag count including subgroups
        /// </summary>
        public int TotalTagCount
        {
            get
            {
                int count = Tags.Count;
                foreach (var sub in SubGroups)
                    count += sub.TotalTagCount;
                return count;
            }
        }
    }

    /// <summary>
    /// Watch window entry - a tag being monitored
    /// </summary>
    public partial class WatchEntry : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _tagId = "";

        [ObservableProperty]
        private string _tagName = "";

        [ObservableProperty]
        private string _tagGroup = "";

        [ObservableProperty]
        private object? _currentValue;

        [ObservableProperty]
        private string _formattedValue = "---";

        [ObservableProperty]
        private DateTime _lastUpdated = DateTime.MinValue;

        [ObservableProperty]
        private bool _isStale;  // True if not updated recently

        [ObservableProperty]
        private bool _hasAlarm;

        [ObservableProperty]
        private string _alarmMessage = "";

        [ObservableProperty]
        private int _updateIntervalMs = 1000;  // Default 1 second
    }
}

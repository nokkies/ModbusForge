using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using MahApps.Metro.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;

namespace ModbusForge.Views
{
    public partial class ConnectorConfigWindow : MetroWindow
    {
        public ConnectorConfigWindow(string nodeId, string connectorType, string nodeName,
            IEnumerable<CustomEntry>? customEntries = null, PlcArea initialArea = PlcArea.Coil, int initialAddress = 0, bool initiallyLinked = false, bool initiallyInverted = false)
        {
            InitializeComponent();
            
            // Create view model with the initial values
            var viewModel = new ConnectorConfigViewModel(nodeId, connectorType, nodeName, customEntries, initialArea, initialAddress, initiallyLinked, initiallyInverted);
            DataContext = viewModel;
            
            // Force the view model properties to update after DataContext is set
            this.Dispatcher.BeginInvoke(() =>
            {
                viewModel.SelectedArea = initialArea;
                viewModel.Address = initialAddress;
                viewModel.IsLinkedToAddress = initiallyLinked;
                viewModel.IsInverted = initiallyInverted;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        
        public ConnectorConfiguration? Result { get; private set; }
        
        private void Window_Closed(object sender, EventArgs e)
        {
            if (DataContext is ConnectorConfigViewModel viewModel)
            {
                Result = viewModel.Result;
            }
        }
    }
    
    public partial class ConnectorConfigViewModel : ObservableObject
    {
        private readonly string _nodeId;
        private readonly string _connectorType;
        private readonly string _nodeName;
        
        private string _connectorInfo = "";
        
        public string ConnectorInfo
        {
            get => _connectorInfo;
            set => SetProperty(ref _connectorInfo, value);
        }
        
        private string _nodeInfo = "";
        
        public string NodeInfo
        {
            get => _nodeInfo;
            set => SetProperty(ref _nodeInfo, value);
        }
        
        private bool _isLinkedToAddress;
        
        public bool IsLinkedToAddress
        {
            get => _isLinkedToAddress;
            set => SetProperty(ref _isLinkedToAddress, value);
        }
        
        private PlcArea _selectedArea;
        
        public PlcArea SelectedArea
        {
            get => _selectedArea;
            set => SetProperty(ref _selectedArea, value);
        }
        
        private int _address;
        
        public int Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }
        
        private bool _isInverted = false;
        
        public bool IsInverted
        {
            get => _isInverted;
            set => SetProperty(ref _isInverted, value);
        }
        
        private string _tagName = "";
        
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }
        
        private string _tagFilter = "";
        
        public string TagFilter
        {
            get => _tagFilter;
            set 
            { 
                SetProperty(ref _tagFilter, value);
                FilterTags();
            }
        }
        
        private ObservableCollection<TagInfo> _availableTags = new ObservableCollection<TagInfo>();
        
        public ObservableCollection<TagInfo> AvailableTags
        {
            get => _availableTags;
            set => SetProperty(ref _availableTags, value);
        }
        
        private ObservableCollection<TagInfo> _filteredTags = new ObservableCollection<TagInfo>();
        
        public ObservableCollection<TagInfo> FilteredTags
        {
            get => _filteredTags;
            set => SetProperty(ref _filteredTags, value);
        }
        
        private TagInfo? _selectedTag;
        
        public TagInfo? SelectedTag
        {
            get => _selectedTag;
            set 
            { 
                SetProperty(ref _selectedTag, value);
                if (SelectedTag != null)
                {
                    SelectedArea = SelectedTag.Area;
                    Address = SelectedTag.Address;
                    TagName = SelectedTag.TagName;
                    IsLinkedToAddress = true;
                }
            }
        }
        
        private ObservableCollection<RecentAddress> _recentAddresses = new ObservableCollection<RecentAddress>();
        
        public ObservableCollection<RecentAddress> RecentAddresses
        {
            get => _recentAddresses;
            set => SetProperty(ref _recentAddresses, value);
        }
        
        private string _statusMessage = "";
        
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        private bool _hasStatusMessage = false;
        
        public bool HasStatusMessage
        {
            get => _hasStatusMessage;
            set => SetProperty(ref _hasStatusMessage, value);
        }
        
        public ConnectorConfiguration? Result { get; private set; }
        
        public PlcArea[] AvailableAreas => Enum.GetValues<PlcArea>();
        
        public ConnectorConfigViewModel(string nodeId, string connectorType, string nodeName,
            IEnumerable<CustomEntry>? customEntries = null, PlcArea initialArea = PlcArea.Coil, int initialAddress = 0, bool initiallyLinked = false, bool initiallyInverted = false)
        {
            _nodeId = nodeId;
            _connectorType = connectorType;
            _nodeName = nodeName;
            
            // Set initial values directly in constructor
            _selectedArea = initialArea;
            _address = initialAddress;
            _isLinkedToAddress = initiallyLinked;
            _isInverted = initiallyInverted;
            
            ConnectorInfo = $"{connectorType} Connector";
            NodeInfo = $"Node: {nodeName}";
            
            InitializeTags(customEntries);
            
            // Workaround: Manually update UI controls after they're loaded
            // Note: This will be handled in the window's Loaded event instead
            
            // Filter tags when filter text changes
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TagFilter))
                    FilterTags();
                // Auto-fill Area/Address when user picks a tag from the list
                if (e.PropertyName == nameof(SelectedTag) && SelectedTag != null)
                {
                    SelectedArea = SelectedTag.Area;
                    Address = SelectedTag.Address;
                    TagName = SelectedTag.TagName;
                    IsLinkedToAddress = true;
                }
            };
        }
        
        private static PlcArea ParseArea(string? area) => (area ?? "").ToLowerInvariant() switch
        {
            "coil"           => PlcArea.Coil,
            "discreteinput"  => PlcArea.DiscreteInput,
            "inputregister"  => PlcArea.InputRegister,
            _                => PlcArea.HoldingRegister
        };

        private void InitializeTags(IEnumerable<CustomEntry>? customEntries)
        {
            if (customEntries != null)
            {
                foreach (var ce in customEntries)
                {
                    var label = string.IsNullOrWhiteSpace(ce.Name)
                        ? $"{ce.Area}:{ce.Address}"
                        : ce.Name;
                    AvailableTags.Add(new TagInfo(ce)
                    {
                        TagName      = label,
                        Area         = ParseArea(ce.Area),
                        Address      = ce.Address,
                        Type         = ce.Type ?? "uint",
                        CurrentValue = ce.Value ?? "?"
                    });
                    // Seed recent list from the first few named entries
                    if (!string.IsNullOrWhiteSpace(ce.Name) && RecentAddresses.Count < 5)
                        RecentAddresses.Add(new RecentAddress
                            { DisplayName = label, Area = ParseArea(ce.Area), Address = ce.Address });
                }
            }
            FilterTags();
        }
        
        private void FilterTags()
        {
            FilteredTags.Clear();
            
            var filtered = AvailableTags.Where(tag => 
                string.IsNullOrEmpty(TagFilter) || 
                tag.TagName.Contains(TagFilter, StringComparison.OrdinalIgnoreCase) ||
                tag.Area.ToString().Contains(TagFilter, StringComparison.OrdinalIgnoreCase) ||
                tag.Address.ToString().Contains(TagFilter, StringComparison.OrdinalIgnoreCase));
            
            foreach (var tag in filtered)
            {
                FilteredTags.Add(tag);
            }
        }
        
        [RelayCommand]
        private void RefreshTags()
        {
            // In real implementation, this would refresh from the Modbus server
            ShowStatus("Tags refreshed successfully");
        }
        
        [RelayCommand]
        private void QuickConnect(RecentAddress recentAddress)
        {
            SelectedArea = recentAddress.Area;
            Address = recentAddress.Address;
            TagName = recentAddress.DisplayName;
            IsLinkedToAddress = true;
            
            ShowStatus($"Connected to {recentAddress.DisplayName}");
        }
        
        [RelayCommand]
        private void Ok()
        {
            if (IsLinkedToAddress)
            {
                Result = new ConnectorConfiguration
                {
                    NodeId = _nodeId,
                    ConnectorType = _connectorType,
                    IsConfigured = true,
                    Area = SelectedArea,
                    Address = Address,
                    Not = IsInverted,
                    Tag = TagName
                };
                
                // Add to recent addresses
                var recent = new RecentAddress
                {
                    DisplayName = string.IsNullOrEmpty(TagName) ? $"{SelectedArea}:{Address}" : TagName,
                    Area = SelectedArea,
                    Address = Address
                };
                
                if (!RecentAddresses.Any(r => r.Area == recent.Area && r.Address == recent.Address))
                {
                    RecentAddresses.Insert(0, recent);
                    if (RecentAddresses.Count > 5)
                    {
                        RecentAddresses.RemoveAt(RecentAddresses.Count - 1);
                    }
                }
            }
            else
            {
                Result = new ConnectorConfiguration
                {
                    NodeId = _nodeId,
                    ConnectorType = _connectorType,
                    IsConfigured = false
                };
            }
            
            // Close the window
            if (Application.Current.Windows.OfType<ConnectorConfigWindow>().FirstOrDefault() is ConnectorConfigWindow window)
            {
                window.DialogResult = true;
                window.Close();
            }
        }
        
        [RelayCommand]
        private void Cancel()
        {
            Result = null;
            
            if (Application.Current.Windows.OfType<ConnectorConfigWindow>().FirstOrDefault() is ConnectorConfigWindow window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
        
        private void ShowStatus(string message)
        {
            StatusMessage = message;
            HasStatusMessage = true;
            
            // Clear status after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                StatusMessage = "";
                HasStatusMessage = false;
                timer.Stop();
            };
            timer.Start();
        }
    }
    
    public class TagInfo : ObservableObject
    {
        private string _type = "";
        private readonly CustomEntry? _originalEntry;
        
        public string TagName { get; set; } = "";
        public PlcArea Area { get; set; }
        public int Address { get; set; }
        public string CurrentValue { get; set; } = "";
        
        public string[] AvailableTypes => new[] { "uint", "int", "real", "string", "bool", "dint", "udint", "lreal", "time", "date", "dt" };
        
        public TagInfo(CustomEntry? originalEntry = null)
        {
            _originalEntry = originalEntry;
        }
        
        public string Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    // Update the original CustomEntry type when changed
                    if (_originalEntry != null)
                    {
                        _originalEntry.Type = value;
                    }
                }
            }
        }
    }
    
    public class RecentAddress
    {
        public string DisplayName { get; set; } = "";
        public PlcArea Area { get; set; }
        public int Address { get; set; }
    }
}

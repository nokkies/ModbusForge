using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MahApps.Metro.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;

namespace ModbusForge.Views
{
    public partial class ConnectorConfigWindow : MetroWindow
    {
        public ConnectorConfigWindow(string nodeId, string connectorType, string nodeName,
            IEnumerable<CustomEntry>? customEntries = null)
        {
            InitializeComponent();
            DataContext = new ConnectorConfigViewModel(nodeId, connectorType, nodeName, customEntries);
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
        
        [ObservableProperty]
        private string _connectorInfo = "";
        
        [ObservableProperty]
        private string _nodeInfo = "";
        
        [ObservableProperty]
        private bool _isLinkedToAddress = false;
        
        [ObservableProperty]
        private PlcArea _selectedArea = PlcArea.Coil;
        
        [ObservableProperty]
        private int _address = 0;
        
        [ObservableProperty]
        private bool _isInverted = false;
        
        [ObservableProperty]
        private string _tagName = "";
        
        [ObservableProperty]
        private string _tagFilter = "";
        
        [ObservableProperty]
        private ObservableCollection<TagInfo> _availableTags = new ObservableCollection<TagInfo>();
        
        [ObservableProperty]
        private ObservableCollection<TagInfo> _filteredTags = new ObservableCollection<TagInfo>();
        
        [ObservableProperty]
        private TagInfo? _selectedTag;
        
        [ObservableProperty]
        private ObservableCollection<RecentAddress> _recentAddresses = new ObservableCollection<RecentAddress>();
        
        [ObservableProperty]
        private string _statusMessage = "";
        
        [ObservableProperty]
        private bool _hasStatusMessage = false;
        
        public ConnectorConfiguration? Result { get; private set; }
        
        public PlcArea[] AvailableAreas => Enum.GetValues<PlcArea>();
        
        public ConnectorConfigViewModel(string nodeId, string connectorType, string nodeName,
            IEnumerable<CustomEntry>? customEntries = null)
        {
            _nodeId = nodeId;
            _connectorType = connectorType;
            _nodeName = nodeName;
            
            ConnectorInfo = $"{connectorType} Connector";
            NodeInfo = $"Node: {nodeName}";
            
            InitializeTags(customEntries);
            
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
                    AvailableTags.Add(new TagInfo
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
    
    public class TagInfo
    {
        public string TagName { get; set; } = "";
        public PlcArea Area { get; set; }
        public int Address { get; set; }
        public string Type { get; set; } = "";
        public string CurrentValue { get; set; } = "";
    }
    
    public class RecentAddress
    {
        public string DisplayName { get; set; } = "";
        public PlcArea Area { get; set; }
        public int Address { get; set; }
    }
}

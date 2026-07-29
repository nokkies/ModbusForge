using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Represents a Program Organization Unit (POU) in the PLC simulation
    /// </summary>
    public partial class ProgramModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();
        
        [ObservableProperty]
        private string _name = "Program";
        
        [ObservableProperty]
        private string _description = "";
        
        [ObservableProperty]
        private bool _isEnabled = true;
        
        [ObservableProperty]
        private int _executionOrder = 0;
        
        [ObservableProperty]
        private ObservableCollection<VisualNode> _nodes = new ObservableCollection<VisualNode>();
        
        [ObservableProperty]
        private ObservableCollection<NodeConnection> _connections = new ObservableCollection<NodeConnection>();
        
        [ObservableProperty]
        private ObservableCollection<ConnectorConfiguration> _connectorConfigs = new ObservableCollection<ConnectorConfiguration>();
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
    }
    
    /// <summary>
    /// Represents a folder/group in the POU tree
    /// </summary>
    public partial class ProgramFolder : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();
        
        [ObservableProperty]
        private string _name = "Folder";
        
        [ObservableProperty]
        private bool _isExpanded = true;
        
        [ObservableProperty]
        private ObservableCollection<ProgramFolder> _folders = new ObservableCollection<ProgramFolder>();
        
        [ObservableProperty]
        private ObservableCollection<ProgramModel> _programs = new ObservableCollection<ProgramModel>();
    }
}

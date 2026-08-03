using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusForge.Models
{
    /// <summary>
    /// Marker interface for items that can appear in the POU tree.
    /// </summary>
    public interface IProgramTreeItem
    {
        string Id { get; }
        string Name { get; set; }
        bool IsRenaming { get; set; }
    }

    /// <summary>
    /// Represents a Program Organization Unit (POU) in the PLC simulation
    /// </summary>
    public partial class ProgramModel : ObservableObject, IProgramTreeItem
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

        [JsonIgnore]
        [ObservableProperty]
        private bool _isRenaming;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime ModifiedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Represents a folder/group in the POU tree
    /// </summary>
    public partial class ProgramFolder : ObservableObject, IProgramTreeItem
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = "Folder";

        [ObservableProperty]
        private bool _isExpanded = true;

        [JsonIgnore]
        [ObservableProperty]
        private bool _isRenaming;

        [ObservableProperty]
        private ObservableCollection<ProgramFolder> _folders = new ObservableCollection<ProgramFolder>();

        [ObservableProperty]
        private ObservableCollection<ProgramModel> _programs = new ObservableCollection<ProgramModel>();

        /// <summary>
        /// Combined, ordered view of <see cref="Folders"/> and <see cref="Programs"/>.
        /// Used by the TreeView to display a mixed hierarchy of folders and programs.
        /// </summary>
        [JsonIgnore]
        public ObservableCollection<IProgramTreeItem> Items { get; } = new ObservableCollection<IProgramTreeItem>();

        private ObservableCollection<ProgramFolder>? _watchedFolders;
        private ObservableCollection<ProgramModel>? _watchedPrograms;

        public ProgramFolder()
        {
            WatchCollection(Folders, ref _watchedFolders, OnChildCollectionChanged);
            WatchCollection(Programs, ref _watchedPrograms, OnChildCollectionChanged);
            RebuildItems();
        }

        partial void OnFoldersChanged(ObservableCollection<ProgramFolder> value)
        {
            WatchCollection(value, ref _watchedFolders, OnChildCollectionChanged);
            RebuildItems();
        }

        partial void OnProgramsChanged(ObservableCollection<ProgramModel> value)
        {
            WatchCollection(value, ref _watchedPrograms, OnChildCollectionChanged);
            RebuildItems();
        }

        private void OnChildCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildItems();
        }

        private static void WatchCollection<T>(
            ObservableCollection<T>? collection,
            ref ObservableCollection<T>? field,
            NotifyCollectionChangedEventHandler handler)
        {
            if (field != null)
            {
                field.CollectionChanged -= handler;
            }

            field = collection;
            if (collection != null)
            {
                collection.CollectionChanged += handler;
            }
        }

        private void RebuildItems()
        {
            Items.Clear();
            if (Folders != null)
            {
                foreach (var folder in Folders)
                {
                    Items.Add(folder);
                }
            }

            if (Programs != null)
            {
                foreach (var program in Programs)
                {
                    Items.Add(program);
                }
            }
        }
    }
}

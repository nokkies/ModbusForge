using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// Describes where a dragged POU tree item should be placed relative to the drop target.
    /// </summary>
    public enum DropPosition
    {
        Into,
        Before,
        After
    }

    /// <summary>
    /// Parameter object used by <see cref="MoveProgramCommand"/> and <see cref="MoveFolderCommand"/>.
    /// </summary>
    public sealed class ProgramTreeMove
    {
        public IProgramTreeItem Source { get; init; } = null!;
        public IProgramTreeItem? Target { get; init; }
        public DropPosition Position { get; init; }
    }

    public partial class VisualNodeEditorViewModel
    {
        [ObservableProperty]
        [JsonIgnore]
        private IProgramTreeItem? _selectedTreeItem;

        public ICommand CreateProgramCommand { get; private set; } = null!;
        public ICommand CreateFolderCommand { get; private set; } = null!;
        public ICommand SelectProgramCommand { get; private set; } = null!;
        public ICommand DeleteProgramCommand { get; private set; } = null!;
        public ICommand DeleteItemCommand { get; private set; } = null!;
        public ICommand DuplicateProgramCommand { get; private set; } = null!;
        public ICommand RenameItemCommand { get; private set; } = null!;
        public ICommand MoveProgramCommand { get; private set; } = null!;
        public ICommand MoveFolderCommand { get; private set; } = null!;

        /// <summary>
        /// The folder that will receive newly created programs or folders.
        /// </summary>
        public ProgramFolder SelectedFolder => SelectedTreeItem switch
        {
            ProgramFolder folder => folder,
            ProgramModel program => FindParentFolder(ProgramTree, program) ?? ProgramTree,
            _ => ProgramTree
        };

        partial void OnSelectedTreeItemChanged(IProgramTreeItem? value)
        {
            if (value is ProgramModel program && !ReferenceEquals(SelectedProgram, program))
            {
                SelectedProgram = program;
            }

            OnPropertyChanged(nameof(SelectedFolder));
            ((IRelayCommand?)DeleteItemCommand)?.NotifyCanExecuteChanged();
            ((IRelayCommand?)DeleteProgramCommand)?.NotifyCanExecuteChanged();
            ((IRelayCommand?)DuplicateProgramCommand)?.NotifyCanExecuteChanged();
            ((IRelayCommand?)RenameItemCommand)?.NotifyCanExecuteChanged();
        }

        partial void InitializeProgramTreeCommands()
        {
            CreateProgramCommand = new RelayCommand(CreateProgram);
            CreateFolderCommand = new RelayCommand(CreateFolder);
            SelectProgramCommand = new RelayCommand<ProgramModel?>(SelectProgram);
            DeleteProgramCommand = new AsyncRelayCommand<IProgramTreeItem?>(DeleteItemAsync, item => item is ProgramModel);
            DeleteItemCommand = new AsyncRelayCommand<IProgramTreeItem?>(DeleteItemAsync, item => item != null && !ReferenceEquals(item, ProgramTree));
            DuplicateProgramCommand = new RelayCommand<IProgramTreeItem?>(DuplicateProgram, item => item is ProgramModel);
            RenameItemCommand = new RelayCommand<IProgramTreeItem?>(RenameItem, item => item != null);
            MoveProgramCommand = new RelayCommand<ProgramTreeMove?>(m => { if (m?.Source is ProgramModel p) MoveProgram(p, m.Target, m.Position); });
            MoveFolderCommand = new RelayCommand<ProgramTreeMove?>(m => { if (m?.Source is ProgramFolder f) MoveFolder(f, m.Target, m.Position); });
        }

        private void CreateProgram()
        {
            var parent = SelectedFolder;
            var name = string.IsNullOrWhiteSpace(NewProgramName)
                ? $"Program {parent.Programs.Count + 1}"
                : NewProgramName.Trim();
            var program = new ProgramModel
            {
                Name = name,
                ExecutionOrder = parent.Programs.Count
            };

            parent.Programs.Add(program);
            NewProgramName = "New Program";
            SelectedTreeItem = program;
            StatusText = $"Created program {program.Name}";
        }

        private void CreateFolder()
        {
            var parent = SelectedFolder;
            var name = string.IsNullOrWhiteSpace(NewProgramName)
                ? $"Folder {parent.Folders.Count + 1}"
                : NewProgramName.Trim();
            var folder = new ProgramFolder { Name = name };

            parent.Folders.Add(folder);
            NewProgramName = "New Program";
            SelectedTreeItem = folder;
            StatusText = $"Created folder {folder.Name}";
        }

        private void SelectProgram(ProgramModel? program)
        {
            if (program != null)
            {
                SelectedTreeItem = program;
            }
        }

        private async Task DeleteItemAsync(IProgramTreeItem? item)
        {
            if (item == null || ReferenceEquals(item, ProgramTree)) return;

            var parent = FindParentFolder(ProgramTree, item);
            if (parent == null) return;

            var message = item is ProgramFolder f
                ? $"Delete folder '{f.Name}' and all its contents?"
                : $"Delete program '{((ProgramModel)item).Name}'?";

            if (_messageBoxService != null)
            {
                var result = await _messageBoxService.ShowAsync(message, "Confirm Delete", DialogButton.YesNo, DialogIcon.Warning);
                if (result != DialogResult.Yes) return;
            }

            var activeWillBeDeleted = false;
            if (item is ProgramFolder folderToDelete)
            {
                activeWillBeDeleted = _activeProgram != null && ContainsItem(folderToDelete, _activeProgram);
            }
            else if (item is ProgramModel programToDelete)
            {
                activeWillBeDeleted = ReferenceEquals(_activeProgram, programToDelete);
            }

            if (item is ProgramModel p)
            {
                parent.Programs.Remove(p);
            }
            else if (item is ProgramFolder f2)
            {
                parent.Folders.Remove(f2);
            }

            var all = EnumeratePrograms(ProgramTree).ToList();
            if (all.Count == 0)
            {
                var main = new ProgramModel { Name = "Main" };
                ProgramTree.Programs.Add(main);
                all.Add(main);
            }

            if (activeWillBeDeleted || _activeProgram == null || FindTreeItem(_activeProgram.Id) == null)
            {
                SelectedTreeItem = all[0];
            }
            else
            {
                SelectedTreeItem = _activeProgram;
            }

            StatusText = $"Deleted {item.Name}";
        }

        private void DuplicateProgram(IProgramTreeItem? item)
        {
            if (item is not ProgramModel program) return;

            var parent = FindParentFolder(ProgramTree, program) ?? ProgramTree;
            var duplicate = new ProgramModel
            {
                Name = $"{program.Name}_Copy",
                Description = program.Description,
                IsEnabled = program.IsEnabled,
                ExecutionOrder = parent.Programs.Count
            };
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var sourceNode in program.Nodes)
            {
                var copy = CloneNode(sourceNode);
                idMap[sourceNode.Id] = copy.Id;
                duplicate.Nodes.Add(copy);
            }

            foreach (var sourceConnection in program.Connections)
            {
                if (!idMap.TryGetValue(sourceConnection.SourceNodeId, out var sourceId)
                    || !idMap.TryGetValue(sourceConnection.TargetNodeId, out var targetId))
                {
                    continue;
                }

                duplicate.Connections.Add(new NodeConnection(sourceId, targetId, sourceConnection.TargetConnector)
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceConnector = sourceConnection.SourceConnector,
                    StartX = sourceConnection.StartX,
                    StartY = sourceConnection.StartY,
                    EndX = sourceConnection.EndX,
                    EndY = sourceConnection.EndY,
                    IsConnected = sourceConnection.IsConnected
                });
            }

            foreach (var sourceConfig in program.ConnectorConfigs)
            {
                if (!idMap.TryGetValue(sourceConfig.NodeId, out var nodeId)) continue;
                duplicate.ConnectorConfigs.Add(new ConnectorConfiguration
                {
                    NodeId = nodeId,
                    ConnectorType = sourceConfig.ConnectorType,
                    IsConfigured = sourceConfig.IsConfigured,
                    Area = sourceConfig.Area,
                    Address = sourceConfig.Address,
                    Not = sourceConfig.Not,
                    Tag = sourceConfig.Tag
                });
            }

            parent.Programs.Add(duplicate);
            SelectedTreeItem = duplicate;
            StatusText = $"Duplicated program {program.Name}";
        }

        private void RenameItem(IProgramTreeItem? item)
        {
            if (item != null)
            {
                item.IsRenaming = true;
            }
        }

        /// <summary>
        /// Finds the parent folder of <paramref name="item"/> within <paramref name="root"/>.
        /// </summary>
        public ProgramFolder? FindParentFolder(ProgramFolder root, IProgramTreeItem item)
        {
            if (ReferenceEquals(root, item)) return null;

            foreach (var child in root.Items)
            {
                if (ReferenceEquals(child, item)) return root;

                if (child is ProgramFolder subFolder)
                {
                    var found = FindParentFolder(subFolder, item);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds any POU tree item by its <see cref="IProgramTreeItem.Id"/>.
        /// </summary>
        public IProgramTreeItem? FindTreeItem(string id)
        {
            if (ProgramTree.Id == id) return ProgramTree;

            foreach (var item in EnumerateTreeItems(ProgramTree))
            {
                if (item.Id == id) return item;
            }

            return null;
        }

        private static IEnumerable<IProgramTreeItem> EnumerateTreeItems(ProgramFolder folder)
        {
            foreach (var child in folder.Items)
            {
                yield return child;

                if (child is ProgramFolder subFolder)
                {
                    foreach (var nested in EnumerateTreeItems(subFolder))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static bool ContainsItem(ProgramFolder folder, IProgramTreeItem item)
        {
            foreach (var child in folder.Items)
            {
                if (ReferenceEquals(child, item)) return true;

                if (child is ProgramFolder subFolder && ContainsItem(subFolder, item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether <paramref name="possibleAncestor"/> is an ancestor of
        /// <paramref name="item"/> (or the item itself).
        /// </summary>
        public bool IsDescendantOf(ProgramFolder possibleAncestor, IProgramTreeItem item)
        {
            if (ReferenceEquals(possibleAncestor, item)) return true;

            var parent = FindParentFolder(ProgramTree, item);
            while (parent != null)
            {
                if (ReferenceEquals(parent, possibleAncestor)) return true;
                parent = FindParentFolder(ProgramTree, parent);
            }

            return false;
        }

        /// <summary>
        /// Moves any POU tree item to the specified target and position.
        /// </summary>
        public void MoveItem(IProgramTreeItem? source, IProgramTreeItem? target, DropPosition position)
        {
            switch (source)
            {
                case ProgramModel program:
                    MoveProgram(program, target, position);
                    break;
                case ProgramFolder folder:
                    MoveFolder(folder, target, position);
                    break;
            }
        }

        private void MoveProgram(ProgramModel program, IProgramTreeItem? target, DropPosition position)
        {
            var sourceFolder = FindParentFolder(ProgramTree, program);
            if (sourceFolder == null) return;

            if (target is ProgramFolder targetFolder)
            {
                // Dropping a program onto a folder always moves it into that folder.
                MoveInList(sourceFolder.Programs, targetFolder.Programs, program, null, false);
                SelectedTreeItem = program;
                StatusText = $"Moved program {program.Name} to folder {targetFolder.Name}";
            }
            else if (target is ProgramModel targetProgram)
            {
                var parentFolder = FindParentFolder(ProgramTree, targetProgram);
                if (parentFolder == null) return;

                MoveInList(sourceFolder.Programs, parentFolder.Programs, program, targetProgram, position == DropPosition.After);
                SelectedTreeItem = program;
                StatusText = $"Moved program {program.Name}";
            }
            else if (target == null)
            {
                MoveInList(sourceFolder.Programs, ProgramTree.Programs, program, null, false);
                SelectedTreeItem = program;
                StatusText = $"Moved program {program.Name} to root";
            }
        }

        private void MoveFolder(ProgramFolder folder, IProgramTreeItem? target, DropPosition position)
        {
            var sourceFolder = FindParentFolder(ProgramTree, folder);
            if (sourceFolder == null) return;

            if (target == null)
            {
                if (ReferenceEquals(folder, ProgramTree)) return;
                MoveInList(sourceFolder.Folders, ProgramTree.Folders, folder, null, false);
                SelectedTreeItem = folder;
                StatusText = $"Moved folder {folder.Name} to root";
            }
            else if (target is ProgramFolder targetFolder)
            {
                if (ReferenceEquals(folder, targetFolder) || IsDescendantOf(folder, targetFolder))
                    return;

                if (position == DropPosition.Into)
                {
                    MoveInList(sourceFolder.Folders, targetFolder.Folders, folder, null, false);
                    SelectedTreeItem = folder;
                    StatusText = $"Moved folder {folder.Name} to {targetFolder.Name}";
                }
                else
                {
                    var parent = FindParentFolder(ProgramTree, targetFolder);
                    if (parent == null) return;

                    MoveInList(sourceFolder.Folders, parent.Folders, folder, targetFolder, position == DropPosition.After);
                    SelectedTreeItem = folder;
                    StatusText = $"Moved folder {folder.Name}";
                }
            }
        }

        private static void MoveInList<T>(
            ObservableCollection<T> sourceList,
            ObservableCollection<T> targetList,
            T item,
            T? target,
            bool after) where T : class
        {
            var sourceIndex = sourceList.IndexOf(item);
            if (sourceIndex < 0) return;

            sourceList.RemoveAt(sourceIndex);

            int targetIndex;
            if (target == null)
            {
                targetIndex = targetList.Count;
            }
            else
            {
                targetIndex = targetList.IndexOf(target);
                if (targetIndex < 0) targetIndex = targetList.Count;
                if (after) targetIndex++;

                // Removing the source from the same list shifted the target index down by one
                // when the source was located before the target.
                if (ReferenceEquals(sourceList, targetList) && sourceIndex < targetIndex)
                {
                    targetIndex--;
                }
            }

            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex > targetList.Count) targetIndex = targetList.Count;

            targetList.Insert(targetIndex, item);
        }
    }
}

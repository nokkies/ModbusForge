using Microsoft.Win32;
using ModbusForge.Models;
using ModbusForge.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModbusForge.Views
{
    public partial class TagBrowserWindow : Window
    {
        private readonly TagService _tagService;
        private readonly IDialogService _dialogService;
        private Tag? _selectedTag;
        private TagGroup? _selectedGroup;
        private bool _isDirty = false;
        private readonly bool _selectionMode;

        public Tag? SelectedTag => _selectedTag;

        public TagBrowserWindow(TagService tagService, IDialogService? dialogService = null, bool selectionMode = false)
        {
            InitializeComponent();
            _tagService = tagService;
            _dialogService = dialogService ?? new NullDialogService();
            _selectionMode = selectionMode;

            // Configure UI based on selection mode
            if (_selectionMode)
            {
                Title = "Select Tag";
                SelectTagButton.Visibility = Visibility.Visible;
                NewGroupButton.Visibility = Visibility.Collapsed;
                NewTagButton.Visibility = Visibility.Collapsed;
                DeleteButton.Visibility = Visibility.Collapsed;
                ImportButton.Visibility = Visibility.Collapsed;
                ExportButton.Visibility = Visibility.Collapsed;
            }

            LoadTreeView();
            UpdateStatus();
        }

        private void LoadTreeView()
        {
            TagTreeView.Items.Clear();

            foreach (var group in _tagService.Groups)
            {
                var groupItem = CreateGroupTreeItem(group);
                TagTreeView.Items.Add(groupItem);
            }

            // Add tags not in any group (no GroupId or GroupId not found)
            var allGroupIds = _tagService.GetAllGroupsFlat().Select(g => g.Id).ToHashSet();
            var ungroupedTags = _tagService.Tags.Where(t =>
                string.IsNullOrEmpty(t.GroupId) || !allGroupIds.Contains(t.GroupId)).ToList();

            if (ungroupedTags.Any())
            {
                var ungroupedItem = new TreeViewItem
                {
                    Header = $"Ungrouped ({ungroupedTags.Count})",
                    Tag = null
                };

                foreach (var tag in ungroupedTags)
                {
                    var tagItem = CreateTagTreeItem(tag);
                    ungroupedItem.Items.Add(tagItem);
                }

                TagTreeView.Items.Add(ungroupedItem);
            }
        }

        private TreeViewItem CreateGroupTreeItem(TagGroup group)
        {
            var item = new TreeViewItem
            {
                Header = $"{group.Name} ({group.Tags.Count + group.SubGroups.Sum(sg => sg.TotalTagCount)})",
                Tag = group,
                FontWeight = System.Windows.FontWeights.Bold
            };

            // Add subgroups
            foreach (var subGroup in group.SubGroups)
            {
                item.Items.Add(CreateGroupTreeItem(subGroup));
            }

            // Add tags in this group
            foreach (var tag in group.Tags)
            {
                item.Items.Add(CreateTagTreeItem(tag));
            }

            return item;
        }

        private TreeViewItem CreateTagTreeItem(Tag tag)
        {
            var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            stack.Children.Add(new TextBlock { Text = "🏷️ ", Margin = new System.Windows.Thickness(0, 0, 4, 0) });
            stack.Children.Add(new TextBlock { Text = tag.Name });
            stack.Children.Add(new TextBlock { Text = $" ({tag.FullAddress})", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11 });

            var item = new TreeViewItem
            {
                Header = stack,
                Tag = tag
            };

            return item;
        }

        private void TagTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item)
            {
                if (item.Tag is Tag tag)
                {
                    _selectedTag = tag;
                    _selectedGroup = null;
                    ShowTagDetails(tag);
                }
                else if (item.Tag is TagGroup group)
                {
                    _selectedTag = null;
                    _selectedGroup = group;
                    DetailsPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _selectedTag = null;
                    _selectedGroup = null;
                    DetailsPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ShowTagDetails(Tag tag)
        {
            DetailsPanel.Visibility = Visibility.Visible;

            // Populate fields
            TagNameBox.Text = tag.Name;
            TagDescriptionBox.Text = tag.Description;
            TagGroupCombo.Text = tag.Group;
            TagAreaCombo.SelectedIndex = (int)tag.Area;
            TagAddressBox.Text = tag.Address.ToString();
            TagDataTypeCombo.SelectedIndex = (int)tag.DataType;
            TagScaleBox.Text = tag.Scale.ToString();
            TagOffsetBox.Text = tag.Offset.ToString();
            TagUnitsBox.Text = tag.Units;

            AlarmEnabledCheck.IsChecked = tag.IsAlarmEnabled;
            AlarmHighBox.Text = tag.AlarmHigh?.ToString() ?? "";
            AlarmLowBox.Text = tag.AlarmLow?.ToString() ?? "";

            // Current values
            CurrentRawValue.Text = tag.CurrentValue?.ToString() ?? "---";
            CurrentScaledValue.Text = tag.ScaledValue?.ToString("F2") ?? "---";

            _isDirty = false;
        }

        private void TagProperty_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _isDirty = true;
        }

        private void TagProperty_Changed(object sender, SelectionChangedEventArgs e)
        {
            _isDirty = true;
        }

        private void AlarmSetting_Changed(object sender, RoutedEventArgs e)
        {
            _isDirty = true;
        }

        private void SaveChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTag == null || !_isDirty) return;

            try
            {
                _selectedTag.Name = TagNameBox.Text;
                _selectedTag.Description = TagDescriptionBox.Text;

                // Resolve group name to GroupId for stable identity
                var groupName = TagGroupCombo.Text;
                _selectedTag.Group = groupName;
                var resolvedGroup = _tagService.FindGroupByName(groupName);
                if (resolvedGroup != null)
                {
                    _selectedTag.GroupId = resolvedGroup.Id;
                }

                _selectedTag.Area = (PlcArea)TagAreaCombo.SelectedIndex;
                _selectedTag.Address = int.Parse(TagAddressBox.Text);
                _selectedTag.DataType = (TagDataType)TagDataTypeCombo.SelectedIndex;
                _selectedTag.Scale = double.Parse(TagScaleBox.Text);
                _selectedTag.Offset = double.Parse(TagOffsetBox.Text);
                _selectedTag.Units = TagUnitsBox.Text;

                _selectedTag.IsAlarmEnabled = AlarmEnabledCheck.IsChecked ?? false;
                _selectedTag.AlarmHigh = string.IsNullOrEmpty(AlarmHighBox.Text) ? null : double.Parse(AlarmHighBox.Text);
                _selectedTag.AlarmLow = string.IsNullOrEmpty(AlarmLowBox.Text) ? null : double.Parse(AlarmLowBox.Text);

                _isDirty = false;
                StatusText.Text = "Changes saved";
                LoadTreeView();
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _dialogService.Show($"Error saving changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void NewGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("New Group", "Enter group name:", "NewGroup");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                // Use parent group name for backward compat; CreateGroup resolves to ParentGroupId internally
                var parentGroup = _selectedGroup?.Name;
                await _tagService.CreateGroup(dialog.InputText, parentGroup);
                LoadTreeView();
                UpdateStatus();
            }
        }

        private async void NewTag_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("New Tag", "Enter tag name:", "NewTag");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                var group = _selectedGroup?.Name ?? "Default";
                var tag = await _tagService.CreateTag(dialog.InputText, group, PlcArea.HoldingRegister, 1, TagDataType.UInt16);
                LoadTreeView();
                UpdateStatus();

                // Select the new tag
                SelectTagInTree(tag);
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTag != null)
            {
                var result = _dialogService.Show($"Delete tag '{_selectedTag.Name}'?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _tagService.DeleteTag(_selectedTag.Id);
                    _selectedTag = null;
                    DetailsPanel.Visibility = Visibility.Collapsed;
                    LoadTreeView();
                    UpdateStatus();
                }
            }
            else if (_selectedGroup != null)
            {
                await DeleteSelectedGroupAsync();
            }
        }

        private async System.Threading.Tasks.Task DeleteSelectedGroupAsync()
        {
            if (_selectedGroup == null) return;

            var preview = _tagService.PreviewGroupDeletion(_selectedGroup.Id);

            if (preview.IsProtected)
            {
                _dialogService.Show(
                    string.IsNullOrEmpty(preview.Message)
                        ? $"Group \"{_selectedGroup.Name}\" is protected and cannot be deleted."
                        : preview.Message,
                    "Cannot Delete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new GroupDeletionDialog(preview) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var deletionResult = await _tagService.DeleteGroupAsync(
                    _selectedGroup.Id,
                    dialog.ChosenMode);

                if (deletionResult.Success)
                {
                    _selectedGroup = null;
                    DetailsPanel.Visibility = Visibility.Collapsed;
                    LoadTreeView();
                    UpdateStatus();
                    StatusText.Text = deletionResult.Message;
                }
                else
                {
                    _dialogService.Show(
                        deletionResult.Message,
                        "Deletion Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusText.Text = "Group deletion failed — data unchanged.";
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                _dialogService.Show(
                    $"Unexpected error during deletion: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusText.Text = "Group deletion failed — data unchanged.";
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Import Tags"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var imported = JsonSerializer.Deserialize<List<Tag>>(json);
                    
                    if (imported != null)
                    {
                        foreach (var tag in imported)
                        {
                            tag.Id = Guid.NewGuid().ToString();  // New ID
                            _tagService.Tags.Add(tag);
                        }
                        
                        LoadTreeView();
                        UpdateStatus();
                        StatusText.Text = $"Imported {imported.Count} tags";
                    }
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _dialogService.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Export Tags",
                FileName = "tags.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_tagService.Tags.ToList(), new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    StatusText.Text = $"Exported {_tagService.Tags.Count} tags";
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    _dialogService.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddToWatch_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTag != null)
            {
                _tagService.AddToWatch(_selectedTag.Id);
                StatusText.Text = $"Added '{_selectedTag.Name}' to watch window";
            }
        }

        private void SelectTagInTree(Tag tag)
        {
            // Expand and select the tag in the tree
            foreach (TreeViewItem item in TagTreeView.Items)
            {
                if (FindAndSelectTag(item, tag))
                    break;
            }
        }

        private bool FindAndSelectTag(TreeViewItem parent, Tag tag)
        {
            foreach (TreeViewItem child in parent.Items)
            {
                if (child.Tag is Tag t && t.Id == tag.Id)
                {
                    child.IsSelected = true;
                    return true;
                }

                if (FindAndSelectTag(child, tag))
                    return true;
            }

            return false;
        }

        private void UpdateStatus()
        {
            TagCountText.Text = $"{_tagService.Tags.Count} tags in {_tagService.Groups.Count} groups";
        }

        private void SelectTag_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTag == null)
            {
                MessageBox.Show("Please select a tag first.", "No Tag Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void TagTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectionMode && _selectedTag != null)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Avalonia.ViewModels
{
    public sealed partial class TagBrowserViewModel : ObservableObject, IDisposable
    {
        private const int DefaultTagAddress = 1;
        private const string DefaultGroupName = "Default";
        private const string ImportFilter =
            "Register maps (*.json;*.csv;*.tsv;*.txt;*.xlsx;*.l5x;*.yaml;*.yml)|*.json;*.csv;*.tsv;*.txt;*.xlsx;*.l5x;*.yaml;*.yml|" +
            "JSON files (*.json)|*.json|CSV files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|" +
            "Excel files (*.xlsx)|*.xlsx|Rockwell L5X (*.l5x)|*.l5x|YAML (*.yaml;*.yml)|*.yaml;*.yml|" +
            "All files (*.*)|*.*";

        private readonly TagService _tagService;
        private readonly IRegisterTemplateImportService _registerTemplateImportService;
        private readonly IRegisterTemplateStore _registerTemplateStore;
        private readonly IFileDialogService _fileDialogService;
        private readonly IFileSystem _fileSystem;
        private readonly IMessageBoxService _messageBoxService;
        private readonly ILogger<TagBrowserViewModel> _logger;
        private ObservableCollection<Tag>? _observedTags;
        private ObservableCollection<TagGroup>? _observedGroups;
        private Tag? _observedSelectedTag;
        private bool _initialized;
        private bool _disposed;
        private bool _isRefreshing;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TagCountSummary))]
        private ObservableCollection<TagTreeItem> _treeItems = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedTag))]
        [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
        [NotifyCanExecuteChangedFor(nameof(AddToWatchCommand))]
        [NotifyCanExecuteChangedFor(nameof(SelectTagCommand))]
        private Tag? _selectedTag;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
        [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
        [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
        private TagGroup? _selectedGroup;

        [ObservableProperty]
        private TagTreeItem? _selectedTreeItem;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editName = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editDescription = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editGroupName = DefaultGroupName;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private PlcArea _editArea = PlcArea.HoldingRegister;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editAddress = DefaultTagAddress.ToString(CultureInfo.InvariantCulture);

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private TagDataType _editDataType = TagDataType.UInt16;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editScale = "1";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editOffset = "0";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editUnits = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private bool _editAlarmEnabled;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editAlarmHigh = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
        private string _editAlarmLow = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RenameGroupCommand))]
        private string _groupNameForRename = string.Empty;

        [ObservableProperty]
        private string _newGroupName = string.Empty;

        [ObservableProperty]
        private string _newTagName = string.Empty;

        [ObservableProperty]
        private GroupDeletionMode _selectedDeletionMode = GroupDeletionMode.MoveToParent;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        public TagBrowserViewModel(
            TagService tagService,
            IRegisterTemplateImportService registerTemplateImportService,
            IRegisterTemplateStore registerTemplateStore,
            IFileDialogService fileDialogService,
            IFileSystem fileSystem,
            IMessageBoxService messageBoxService,
            ILogger<TagBrowserViewModel>? logger = null,
            bool selectionMode = false)
        {
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _registerTemplateImportService = registerTemplateImportService ?? throw new ArgumentNullException(nameof(registerTemplateImportService));
            _registerTemplateStore = registerTemplateStore ?? throw new ArgumentNullException(nameof(registerTemplateStore));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _messageBoxService = messageBoxService ?? throw new ArgumentNullException(nameof(messageBoxService));
            _logger = logger ?? NullLogger<TagBrowserViewModel>.Instance;
            SelectionMode = selectionMode;

            InitializeCommand = new AsyncRelayCommand(() => InitializeAsync());
            NewGroupCommand = new AsyncRelayCommand(CreateGroupAsync);
            NewTagCommand = new AsyncRelayCommand(CreateTagAsync);
            DeleteCommand = new AsyncRelayCommand(DeleteSelectedAsync, CanDelete);
            RenameGroupCommand = new AsyncRelayCommand(RenameGroupAsync, CanRenameGroup);
            SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, CanSaveChanges);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            ImportTemplateCommand = new RelayCommand(() => RequestTemplateImport(null));
            SaveTemplateCommand = new AsyncRelayCommand(SaveCsvTemplateAsync);
            AddToWatchCommand = new RelayCommand(AddSelectedToWatch, CanAddToWatch);
            SelectTagCommand = new RelayCommand(AcceptSelectedTag, CanSelectTag);

            _tagService.PropertyChanged += OnTagServicePropertyChanged;
            AttachCollections();
            RefreshTree();
        }

        public IAsyncRelayCommand InitializeCommand { get; }
        public IAsyncRelayCommand NewGroupCommand { get; }
        public IAsyncRelayCommand NewTagCommand { get; }
        public IAsyncRelayCommand DeleteCommand { get; }
        public IAsyncRelayCommand RenameGroupCommand { get; }
        public IAsyncRelayCommand SaveChangesCommand { get; }
        public IAsyncRelayCommand ImportCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand ImportTemplateCommand { get; }
        public IAsyncRelayCommand SaveTemplateCommand { get; }
        public IRelayCommand AddToWatchCommand { get; }
        public IRelayCommand SelectTagCommand { get; }

        public ObservableCollection<Tag> Tags => _tagService.Tags;

        public ObservableCollection<TagGroup> Groups => _tagService.Groups;

        public ObservableCollection<string> GroupNames { get; } = new();

        public IReadOnlyList<PlcArea> AreaOptions { get; } = Enum.GetValues<PlcArea>();

        public IReadOnlyList<TagDataType> DataTypeOptions { get; } = Enum.GetValues<TagDataType>();

        public IReadOnlyList<GroupDeletionMode> GroupDeletionModes { get; } = Enum.GetValues<GroupDeletionMode>();

        public bool SelectionMode { get; }

        public bool HasSelectedTag => SelectedTag != null;

        public bool HasSelectedGroup => SelectedGroup != null;

        public string TagCountSummary => $"{_tagService.Tags.Count} tags in {GetAllGroups().Count()} groups";

        public string CurrentRawValue => SelectedTag?.CurrentValue?.ToString() ?? "---";

        public string CurrentScaledValue => SelectedTag?.ScaledValue?.ToString("F2", CultureInfo.CurrentCulture) ?? "---";

        public TagService TagService => _tagService;

        public IRegisterTemplateImportService RegisterTemplateImportService => _registerTemplateImportService;

        public IRegisterTemplateStore RegisterTemplateStore => _registerTemplateStore;

        public IFileDialogService FileDialogService => _fileDialogService;

        public IFileSystem FileSystem => _fileSystem;

        public IMessageBoxService MessageBoxService => _messageBoxService;

        public event EventHandler<bool>? RequestClose;

        public event EventHandler<TemplateImportRequestedEventArgs>? TemplateImportRequested;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || _initialized)
                return;

            try
            {
                IsBusy = true;
                StatusMessage = "Loading tags...";
                await _tagService.InitializeAsync(cancellationToken);
                _initialized = true;
                RefreshTree();
                StatusMessage = "Ready";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Loading tags cancelled.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to initialize the tag browser");
                StatusMessage = "Tag loading failed.";
                await ShowMessageAsync($"Could not load tags: {ex.Message}", "Tag Browser", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void RefreshTree()
        {
            if (_disposed || _isRefreshing)
                return;

            _isRefreshing = true;
            try
            {
                var selectedTagId = SelectedTag?.Id;
                var selectedGroupId = SelectedGroup?.Id;
                var nextItems = new ObservableCollection<TagTreeItem>();

                foreach (var group in _tagService.Groups)
                    nextItems.Add(BuildGroupItem(group));

                var groupIds = GetAllGroups().Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
                var ungroupedTags = _tagService.Tags
                    .Where(tag => string.IsNullOrWhiteSpace(tag.GroupId) || !groupIds.Contains(tag.GroupId!))
                    .ToList();

                if (ungroupedTags.Count > 0)
                {
                    var ungrouped = new TagTreeItem("Ungrouped", null, null, isPlaceholder: true);
                    foreach (var tag in ungroupedTags)
                        ungrouped.Children.Add(TagTreeItem.ForTag(tag));
                    nextItems.Add(ungrouped);
                }

                TreeItems = nextItems;
                GroupNames.Clear();
                foreach (var groupName in GetAllGroups()
                    .Select(group => group.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
                {
                    GroupNames.Add(groupName);
                }

                if (selectedTagId != null && _tagService.Tags.All(tag => tag.Id != selectedTagId))
                {
                    SelectedTag = null;
                    SelectedGroup = null;
                    SelectedTreeItem = null;
                }
                else if (selectedGroupId != null && GetAllGroups().All(group => group.Id != selectedGroupId))
                {
                    SelectedTag = null;
                    SelectedGroup = null;
                    SelectedTreeItem = null;
                }

                OnPropertyChanged(nameof(TagCountSummary));
                OnPropertyChanged(nameof(CurrentRawValue));
                OnPropertyChanged(nameof(CurrentScaledValue));
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public async Task MergeImportedTemplateAsync(
            RegisterTemplate template,
            IReadOnlyList<RegisterTemplateEntry> entries,
            bool saveTemplate)
        {
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(entries);

            try
            {
                IsBusy = true;
                var added = _registerTemplateImportService.Merge(_tagService, entries, out var skipped);
                RefreshTree();
                StatusMessage = $"Imported {added.Count} tags from template '{template.Name}'.";

                var summary = $"Imported {added.Count} tags from '{template.Name}'.";
                if (skipped.Count > 0)
                    summary += Environment.NewLine +
                        $"Skipped {skipped.Count} duplicate tag(s): {string.Join(", ", skipped.Take(10))}";

                if (saveTemplate)
                {
                    try
                    {
                        var savedPath = _registerTemplateStore.Save(template);
                        summary += Environment.NewLine + $"Template saved to {savedPath}";
                    }
                    catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
                    {
                        _logger.LogError(ex, "Failed to save imported register template {TemplateName}", template.Name);
                        summary += Environment.NewLine + $"The template could not be saved: {ex.Message}";
                    }
                }

                await ShowMessageAsync(summary, "Import Complete", DialogButton.Ok, DialogIcon.Information);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to merge register template {TemplateName}", template.Name);
                StatusMessage = "Template import failed.";
                await ShowMessageAsync($"Import failed: {ex.Message}", "Import Failed", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void AcceptSelectedTag()
        {
            if (!CanSelectTag())
            {
                _ = ShowMessageAsync("Please select a tag first.", "No Tag Selected", DialogButton.Ok, DialogIcon.Information);
                return;
            }

            RequestClose?.Invoke(this, true);
        }

        public void CancelSelection() => RequestClose?.Invoke(this, false);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _tagService.PropertyChanged -= OnTagServicePropertyChanged;
            DetachSelectedTag();
            DetachCollections();
            GC.SuppressFinalize(this);
        }

        private async Task CreateGroupAsync()
        {
            var name = NewGroupName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await ShowMessageAsync("Enter a name for the new group.", "New Group", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            if (FindGroupByName(name) != null)
            {
                await ShowMessageAsync($"A group named '{name}' already exists.", "New Group", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            try
            {
                IsBusy = true;
                var parentName = SelectedGroup?.Name;
                var group = await _tagService.CreateGroup(name, parentName);
                NewGroupName = string.Empty;
                RefreshTree();
                SelectGroup(group);
                StatusMessage = $"Created group '{group.Name}'.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to create group {GroupName}", name);
                await ShowMessageAsync($"Failed to create group: {ex.Message}", "New Group", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CreateTagAsync()
        {
            var name = NewTagName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                await ShowMessageAsync("Enter a name for the new tag.", "New Tag", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            if (_tagService.GetTagByName(name) != null)
            {
                await ShowMessageAsync($"A tag named '{name}' already exists.", "New Tag", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            try
            {
                IsBusy = true;
                var groupName = SelectedGroup?.Name ?? DefaultGroupName;
                var tag = await _tagService.CreateTag(name, groupName, PlcArea.HoldingRegister, DefaultTagAddress, TagDataType.UInt16);
                NewTagName = string.Empty;
                RefreshTree();
                SelectTag(tag);
                StatusMessage = $"Created tag '{tag.Name}'.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to create tag {TagName}", name);
                await ShowMessageAsync($"Failed to create tag: {ex.Message}", "New Tag", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteSelectedAsync()
        {
            if (SelectedTag != null)
            {
                var result = await _messageBoxService.ShowAsync(
                    $"Delete tag '{SelectedTag.Name}'?",
                    "Confirm Delete",
                    DialogButton.YesNo,
                    DialogIcon.Question);
                if (result != DialogResult.Yes)
                    return;

                try
                {
                    IsBusy = true;
                    await _tagService.DeleteTag(SelectedTag.Id);
                    SelectedTag = null;
                    SelectedGroup = null;
                    SelectedTreeItem = null;
                    RefreshTree();
                    StatusMessage = "Tag deleted.";
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
                {
                    _logger.LogError(ex, "Failed to delete tag");
                    await ShowMessageAsync($"Failed to delete tag: {ex.Message}", "Delete", DialogButton.Ok, DialogIcon.Error);
                }
                finally
                {
                    IsBusy = false;
                }

                return;
            }

            if (SelectedGroup == null)
                return;

            var preview = _tagService.PreviewGroupDeletion(SelectedGroup.Id);
            if (preview.IsProtected)
            {
                await ShowMessageAsync(
                    string.IsNullOrWhiteSpace(preview.Message)
                        ? $"Group '{SelectedGroup.Name}' cannot be deleted."
                        : preview.Message,
                    "Cannot Delete",
                    DialogButton.Ok,
                    DialogIcon.Warning);
                return;
            }

            var message = $"Delete group '{SelectedGroup.Name}'?" + Environment.NewLine +
                          $"This affects {preview.RecursiveTagCount} tag(s) and {preview.RecursiveSubgroupCount} subgroup(s).";
            if (SelectedDeletionMode == GroupDeletionMode.CascadeDelete)
                message += Environment.NewLine + "Cascade delete permanently removes the affected tags.";

            var confirmation = await _messageBoxService.ShowAsync(message, "Confirm Group Delete", DialogButton.YesNo, DialogIcon.Question);
            if (confirmation != DialogResult.Yes)
                return;

            try
            {
                IsBusy = true;
                var deletionResult = await _tagService.DeleteGroupAsync(SelectedGroup.Id, SelectedDeletionMode);
                if (deletionResult.Success)
                {
                    SelectedTag = null;
                    SelectedGroup = null;
                    SelectedTreeItem = null;
                    RefreshTree();
                    StatusMessage = deletionResult.Message;
                }
                else
                {
                    StatusMessage = "Group deletion failed; data was not changed.";
                    await ShowMessageAsync(deletionResult.Message, "Deletion Failed", DialogButton.Ok, DialogIcon.Error);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to delete group");
                await ShowMessageAsync($"Failed to delete group: {ex.Message}", "Delete", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RenameGroupAsync()
        {
            if (SelectedGroup == null || string.IsNullOrWhiteSpace(GroupNameForRename))
                return;

            var name = GroupNameForRename.Trim();
            var existing = FindGroupByName(name);
            if (existing != null && existing.Id != SelectedGroup.Id)
            {
                await ShowMessageAsync($"A group named '{name}' already exists.", "Rename Group", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            try
            {
                IsBusy = true;
                await _tagService.RenameGroup(SelectedGroup.Id, name);
                SelectedGroup.Name = name;
                GroupNameForRename = name;
                RefreshTree();
                StatusMessage = $"Renamed group to '{name}'.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to rename group {GroupId}", SelectedGroup.Id);
                await ShowMessageAsync($"Failed to rename group: {ex.Message}", "Rename Group", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveChangesAsync()
        {
            var selectedTag = SelectedTag;
            if (selectedTag == null)
                return;

            if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditGroupName))
            {
                await ShowMessageAsync("Tag name and group are required.", "Tag Properties", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            if (_tagService.Tags.Any(tag => tag.Id != selectedTag.Id &&
                string.Equals(tag.Name, EditName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                await ShowMessageAsync($"A tag named '{EditName.Trim()}' already exists.", "Tag Properties", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            if (!TryParseInt(EditAddress, out var address) || address < 0 || address > 65535)
            {
                await ShowMessageAsync("Address must be an integer between 0 and 65535.", "Tag Properties", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            if (!TryParseDouble(EditScale, out var scale) || !TryParseDouble(EditOffset, out var offset))
            {
                await ShowMessageAsync("Scale and offset must be valid numbers.", "Tag Properties", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            if (!TryParseOptionalDouble(EditAlarmHigh, out var alarmHigh) || !TryParseOptionalDouble(EditAlarmLow, out var alarmLow))
            {
                await ShowMessageAsync("Alarm limits must be valid numbers or blank.", "Tag Properties", DialogButton.Ok, DialogIcon.Warning);
                return;
            }

            try
            {
                IsBusy = true;
                var tag = selectedTag;
                var oldGroup = FindGroupById(tag.GroupId) ?? FindGroupByName(tag.Group);
                var newGroup = FindGroupByName(EditGroupName.Trim());
                if (newGroup == null)
                    newGroup = await _tagService.CreateGroup(EditGroupName.Trim());

                if (oldGroup != null && oldGroup.Id != newGroup.Id)
                    oldGroup.Tags.Remove(tag);
                if (!newGroup.Tags.Contains(tag))
                    newGroup.Tags.Add(tag);

                tag.Name = EditName.Trim();
                tag.Description = EditDescription.Trim();
                tag.Group = newGroup.Name;
                tag.GroupId = newGroup.Id;
                tag.Area = EditArea;
                tag.Address = address;
                tag.DataType = EditDataType;
                tag.Scale = scale;
                tag.Offset = offset;
                tag.Units = EditUnits.Trim();
                tag.IsAlarmEnabled = EditAlarmEnabled;
                tag.AlarmHigh = alarmHigh;
                tag.AlarmLow = alarmLow;

                RefreshTree();
                OnPropertyChanged(nameof(CurrentRawValue));
                OnPropertyChanged(nameof(CurrentScaledValue));
                StatusMessage = "Tag changes saved.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to save tag changes");
                await ShowMessageAsync($"Error saving changes: {ex.Message}", "Tag Properties", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ImportAsync()
        {
            var path = await _fileDialogService.ShowOpenFileDialogAsync("Import Tags", ImportFilter);
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                await ImportJsonAsync(path);
            }
            else
            {
                RequestTemplateImport(path);
            }
        }

        private async Task ImportJsonAsync(string path)
        {
            try
            {
                if (!_fileSystem.FileExists(path))
                    throw new FileNotFoundException("The selected file could not be found.", path);

                IsBusy = true;
                var json = await _fileSystem.ReadAllTextAsync(path);
                var imported = JsonSerializer.Deserialize<List<Tag>>(json) ?? new List<Tag>();
                var added = 0;

                foreach (var tag in imported)
                {
                    if (string.IsNullOrWhiteSpace(tag.Name) || _tagService.GetTagByName(tag.Name) != null)
                        continue;

                    var groupName = string.IsNullOrWhiteSpace(tag.Group) ? DefaultGroupName : tag.Group.Trim();
                    var group = FindGroupById(tag.GroupId) ?? FindGroupByName(groupName);
                    if (group == null)
                    {
                        group = new TagGroup { Name = groupName };
                        _tagService.Groups.Add(group);
                    }

                    tag.Id = Guid.NewGuid().ToString();
                    tag.Group = group.Name;
                    tag.GroupId = group.Id;
                    _tagService.Tags.Add(tag);
                    group.Tags.Add(tag);
                    added++;
                }

                RefreshTree();
                StatusMessage = $"Imported {added} tag(s).";
                await ShowMessageAsync($"Imported {added} tag(s) from '{Path.GetFileName(path)}'.", "Import Complete", DialogButton.Ok, DialogIcon.Information);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to import tags from {Path}", path);
                await ShowMessageAsync($"Import failed: {ex.Message}", "Import Failed", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportAsync()
        {
            var path = await _fileDialogService.ShowSaveFileDialogAsync("Export Tags", "JSON files (*.json)|*.json|All files (*.*)|*.*", "tags.json");
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                IsBusy = true;
                var json = JsonSerializer.Serialize(_tagService.Tags.ToList(), new JsonSerializerOptions { WriteIndented = true });
                await _fileSystem.WriteAllTextAsync(path, json);
                StatusMessage = $"Exported {_tagService.Tags.Count} tag(s).";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to export tags to {Path}", path);
                await ShowMessageAsync($"Export failed: {ex.Message}", "Export Failed", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveCsvTemplateAsync()
        {
            var path = await _fileDialogService.ShowSaveFileDialogAsync(
                "Save Register Map Template",
                "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                "register-map-template.csv");
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                IsBusy = true;
                await _fileSystem.WriteAllTextAsync(path, _registerTemplateImportService.GetCsvTemplate());
                StatusMessage = $"Template saved to {Path.GetFileName(path)}.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to save the CSV register template to {Path}", path);
                await ShowMessageAsync($"Failed to save template: {ex.Message}", "Save Template", DialogButton.Ok, DialogIcon.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void AddSelectedToWatch()
        {
            if (SelectedTag == null)
                return;

            try
            {
                _tagService.AddToWatch(SelectedTag.Id);
                StatusMessage = $"Added '{SelectedTag.Name}' to the watch window.";
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                _logger.LogError(ex, "Failed to add tag {TagId} to watch", SelectedTag.Id);
                _ = ShowMessageAsync($"Could not add tag to watch: {ex.Message}", "Watch", DialogButton.Ok, DialogIcon.Error);
            }
        }

        private bool CanDelete() => SelectedTag != null || SelectedGroup != null;

        private bool CanRenameGroup() => SelectedGroup != null && !string.IsNullOrWhiteSpace(GroupNameForRename);

        private bool CanSaveChanges() => SelectedTag != null;

        private bool CanAddToWatch() => SelectedTag != null;

        private bool CanSelectTag() => SelectionMode && SelectedTag != null;

        partial void OnSelectedTreeItemChanged(TagTreeItem? value)
        {
            if (value?.Tag != null)
            {
                SelectTag(value.Tag);
            }
            else if (value?.Group != null)
            {
                SelectGroup(value.Group);
            }
            else
            {
                SelectedTag = null;
                SelectedGroup = null;
                ClearTagEditor();
            }
        }

        partial void OnSelectedTagChanged(Tag? value)
        {
            if (_observedSelectedTag != value)
            {
                DetachSelectedTag();
                _observedSelectedTag = value;
                if (_observedSelectedTag != null)
                    _observedSelectedTag.PropertyChanged += SelectedTag_PropertyChanged;
            }

            OnPropertyChanged(nameof(CurrentRawValue));
            OnPropertyChanged(nameof(CurrentScaledValue));
        }

        private void SelectTag(Tag tag)
        {
            SelectedTag = tag;
            SelectedGroup = null;
            EditName = tag.Name;
            EditDescription = tag.Description;
            EditGroupName = tag.Group;
            EditArea = tag.Area;
            EditAddress = tag.Address.ToString(CultureInfo.InvariantCulture);
            EditDataType = tag.DataType;
            EditScale = tag.Scale.ToString(CultureInfo.CurrentCulture);
            EditOffset = tag.Offset.ToString(CultureInfo.CurrentCulture);
            EditUnits = tag.Units;
            EditAlarmEnabled = tag.IsAlarmEnabled;
            EditAlarmHigh = tag.AlarmHigh?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            EditAlarmLow = tag.AlarmLow?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            SelectTagInTree(tag);
        }

        private void SelectGroup(TagGroup group)
        {
            DetachSelectedTag();
            SelectedGroup = group;
            SelectedTag = null;
            GroupNameForRename = group.Name;
            ClearTagEditor();
            SelectGroupInTree(group);
        }

        private void SelectedTag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CurrentRawValue));
            OnPropertyChanged(nameof(CurrentScaledValue));
            if (e.PropertyName is nameof(Tag.Name) or nameof(Tag.Group) or nameof(Tag.GroupId) or nameof(Tag.Area) or nameof(Tag.Address))
                RefreshTree();
        }

        private void DetachSelectedTag()
        {
            if (_observedSelectedTag != null)
                _observedSelectedTag.PropertyChanged -= SelectedTag_PropertyChanged;
            _observedSelectedTag = null;
        }

        private void ClearTagEditor()
        {
            EditName = string.Empty;
            EditDescription = string.Empty;
            EditGroupName = DefaultGroupName;
            EditArea = PlcArea.HoldingRegister;
            EditAddress = DefaultTagAddress.ToString(CultureInfo.InvariantCulture);
            EditDataType = TagDataType.UInt16;
            EditScale = "1";
            EditOffset = "0";
            EditUnits = string.Empty;
            EditAlarmEnabled = false;
            EditAlarmHigh = string.Empty;
            EditAlarmLow = string.Empty;
            OnPropertyChanged(nameof(CurrentRawValue));
            OnPropertyChanged(nameof(CurrentScaledValue));
        }

        private TagTreeItem BuildGroupItem(TagGroup group)
        {
            var item = TagTreeItem.ForGroup(group);
            foreach (var subGroup in group.SubGroups)
                item.Children.Add(BuildGroupItem(subGroup));
            foreach (var tag in group.Tags)
                item.Children.Add(TagTreeItem.ForTag(tag));
            return item;
        }

        private IEnumerable<TagGroup> GetAllGroups()
        {
            foreach (var group in _tagService.Groups)
            {
                yield return group;
                foreach (var child in FlattenGroups(group))
                    yield return child;
            }
        }

        private static IEnumerable<TagGroup> FlattenGroups(TagGroup group)
        {
            foreach (var child in group.SubGroups)
            {
                yield return child;
                foreach (var nested in FlattenGroups(child))
                    yield return nested;
            }
        }

        private TagGroup? FindGroupById(string? groupId) =>
            string.IsNullOrWhiteSpace(groupId)
                ? null
                : GetAllGroups().FirstOrDefault(group => group.Id == groupId);

        private TagGroup? FindGroupByName(string? groupName) =>
            string.IsNullOrWhiteSpace(groupName)
                ? null
                : GetAllGroups().FirstOrDefault(group => string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));

        private void SelectGroupInTree(TagGroup group)
        {
            var item = FindTreeItem(TreeItems, candidate => candidate.Group?.Id == group.Id);
            if (item != null)
                SelectedTreeItem = item;
        }

        private void SelectTagInTree(Tag tag)
        {
            var item = FindTreeItem(TreeItems, candidate => candidate.Tag?.Id == tag.Id);
            if (item != null)
                SelectedTreeItem = item;
        }

        private static TagTreeItem? FindTreeItem(IEnumerable<TagTreeItem> items, Func<TagTreeItem, bool> predicate)
        {
            foreach (var item in items)
            {
                if (predicate(item))
                    return item;
                var match = FindTreeItem(item.Children, predicate);
                if (match != null)
                    return match;
            }

            return null;
        }

        private void RequestTemplateImport(string? filePath) =>
            TemplateImportRequested?.Invoke(this, new TemplateImportRequestedEventArgs(filePath));

        private void OnTagServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TagService.Tags) || e.PropertyName == nameof(TagService.Groups))
            {
                AttachCollections();
                RefreshTree();
                OnPropertyChanged(nameof(Tags));
                OnPropertyChanged(nameof(Groups));
                OnPropertyChanged(nameof(TagCountSummary));
            }
        }

        private void AttachCollections()
        {
            if (_observedTags != _tagService.Tags)
            {
                if (_observedTags != null)
                    _observedTags.CollectionChanged -= OnTagsCollectionChanged;
                _observedTags = _tagService.Tags;
                _observedTags.CollectionChanged += OnTagsCollectionChanged;
            }

            if (_observedGroups != _tagService.Groups)
            {
                if (_observedGroups != null)
                    _observedGroups.CollectionChanged -= OnGroupsCollectionChanged;
                _observedGroups = _tagService.Groups;
                _observedGroups.CollectionChanged += OnGroupsCollectionChanged;
            }
        }

        private void DetachCollections()
        {
            if (_observedTags != null)
                _observedTags.CollectionChanged -= OnTagsCollectionChanged;
            if (_observedGroups != null)
                _observedGroups.CollectionChanged -= OnGroupsCollectionChanged;
            _observedTags = null;
            _observedGroups = null;
        }

        private void OnTagsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshTree();
            OnPropertyChanged(nameof(TagCountSummary));
        }

        private void OnGroupsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshTree();
            OnPropertyChanged(nameof(TagCountSummary));
        }

        private static bool TryParseInt(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryParseDouble(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        private static bool TryParseOptionalDouble(string text, out double? value)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                value = null;
                return true;
            }

            if (TryParseDouble(text, out var parsed))
            {
                value = parsed;
                return true;
            }

            value = null;
            return false;
        }

        private async Task ShowMessageAsync(string message, string title, DialogButton button, DialogIcon icon) =>
            await _messageBoxService.ShowAsync(message, title, button, icon);

        public sealed class TemplateImportRequestedEventArgs : EventArgs
        {
            public TemplateImportRequestedEventArgs(string? filePath) => FilePath = filePath;

            public string? FilePath { get; }
        }

        public sealed class TagTreeItem
        {
            public TagTreeItem(string displayName, Tag? tag, TagGroup? group, bool isPlaceholder = false)
            {
                DisplayName = displayName;
                Tag = tag;
                Group = group;
                IsPlaceholder = isPlaceholder;
            }

            public static TagTreeItem ForGroup(TagGroup group) => new(group.Name, null, group, false);

            public static TagTreeItem ForTag(Tag tag) => new(tag.Name, tag, null, false);

            public string DisplayName { get; }

            public Tag? Tag { get; }

            public TagGroup? Group { get; }

            public bool IsGroup => Group != null;

            public bool IsTag => Tag != null;

            public bool IsPlaceholder { get; }

            public string AddressText => Tag?.FullAddress ?? string.Empty;

            public string CountText => Group != null ? $"({Group.TotalTagCount})" : string.Empty;

            public ObservableCollection<TagTreeItem> Children { get; } = new();
        }
    }
}

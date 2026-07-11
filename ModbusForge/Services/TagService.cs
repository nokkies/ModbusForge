using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusForge.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Persistence wrapper with schema versioning for the tag database.
    /// </summary>
    public class TagDatabase
    {
        public int SchemaVersion { get; set; } = 2;
        public List<Tag> Tags { get; set; } = new();
        public List<TagGroup> Groups { get; set; } = new();
    }

    /// <summary>
    /// Service for managing the tag database - symbolic addressing for Modbus registers
    /// </summary>
    public partial class TagService : ObservableObject
    {
        private string _tagsFilePath = null!;
        private JsonSerializerOptions _jsonOptions = null!;
        private readonly ILogger<Tag> _tagLogger;
        private bool _initialized;

        [ObservableProperty]
        private ObservableCollection<Tag> _tags = new();

        [ObservableProperty]
        private ObservableCollection<TagGroup> _groups = new();

        [ObservableProperty]
        private ObservableCollection<WatchEntry> _watchEntries = new();

        public TagService()
        {
            _tagLogger = NullLogger<Tag>.Instance;
            SetupPaths();
        }

        public TagService(ILogger<Tag> tagLogger)
        {
            _tagLogger = tagLogger ?? NullLogger<Tag>.Instance;
            SetupPaths();
        }

        private void SetupPaths()
        {
            _tagsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ModbusForge",
                "tags.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Create default group
            Groups.Add(new TagGroup { Name = "Default", Description = "Default tag group" });
        }

        /// <summary>
        /// Initializes the tag service by loading persisted data.
        /// Must be called before using the service. Safe to call multiple times.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
                return;

            await LoadTagsAsync(cancellationToken);
            _initialized = true;
        }

        /// <summary>
        /// Create a new tag with symbolic name
        /// </summary>
        public async Task<Tag> CreateTag(string name, string group, PlcArea area, int address, TagDataType dataType)
        {
            // Resolve group to ID
            var targetGroup = FindGroupByName(group);
            if (targetGroup == null)
            {
                targetGroup = EnsureGroupExists(group);
            }

            var tag = new Tag(_tagLogger)
            {
                Name = name,
                Group = group,
                GroupId = targetGroup.Id,
                Area = area,
                Address = address,
                DataType = dataType
            };

            Tags.Add(tag);

            // Add to group's tag collection
            targetGroup.Tags.Add(tag);

            await SaveTagsAsync();

            return tag;
        }

        /// <summary>
        /// Delete a tag by ID
        /// </summary>
        public async Task DeleteTag(string tagId)
        {
            var tag = Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag != null)
            {
                Tags.Remove(tag);

                // Remove from group's tag collection
                if (!string.IsNullOrEmpty(tag.GroupId))
                {
                    var group = FindGroupById(tag.GroupId);
                    group?.Tags.Remove(tag);
                }

                // Remove from watch entries too
                var watchEntry = WatchEntries.FirstOrDefault(w => w.TagId == tagId);
                if (watchEntry != null)
                    WatchEntries.Remove(watchEntry);

                await SaveTagsAsync();
            }
        }

        /// <summary>
        /// Get tag by symbolic name
        /// </summary>
        public Tag? GetTagByName(string name)
        {
            return Tags.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get tag by address
        /// </summary>
        public Tag? GetTagByAddress(PlcArea area, int address)
        {
            return Tags.FirstOrDefault(t => t.Area == area && t.Address == address);
        }

        /// <summary>
        /// Resolve symbolic name to address
        /// </summary>
        public (PlcArea area, int address)? ResolveSymbolicAddress(string symbolicName)
        {
            var tag = GetTagByName(symbolicName);
            if (tag != null)
                return (tag.Area, tag.Address);

            return null;
        }

        /// <summary>
        /// Add a tag to the watch window
        /// </summary>
        public WatchEntry AddToWatch(string tagId, int updateIntervalMs = 1000)
        {
            var tag = Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag == null) throw new ArgumentException("Tag not found");

            // Check if already watching
            var existing = WatchEntries.FirstOrDefault(w => w.TagId == tagId);
            if (existing != null) return existing;

            var entry = new WatchEntry
            {
                TagId = tagId,
                TagName = tag.Name,
                TagGroup = tag.Group,
                UpdateIntervalMs = updateIntervalMs
            };

            WatchEntries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Remove a tag from the watch window
        /// </summary>
        public void RemoveFromWatch(string watchEntryId)
        {
            var entry = WatchEntries.FirstOrDefault(w => w.Id == watchEntryId);
            if (entry != null)
                WatchEntries.Remove(entry);
        }

        /// <summary>
        /// Update tag value (called by Modbus service when reading)
        /// </summary>
        public void UpdateTagValue(PlcArea area, int address, object value)
        {
            var tag = GetTagByAddress(area, address);
            if (tag != null)
            {
                tag.CurrentValue = value;
                tag.LastUpdated = DateTime.Now;

                // Update watch entry if exists
                var watchEntry = WatchEntries.FirstOrDefault(w => w.TagId == tag.Id);
                if (watchEntry != null)
                {
                    watchEntry.CurrentValue = value;
                    watchEntry.FormattedValue = tag.FormattedValue;
                    watchEntry.LastUpdated = DateTime.Now;
                    watchEntry.IsStale = false;

                    // Check alarms
                    if (tag.IsAlarmEnabled && tag.ScaledValue.HasValue)
                    {
                        var scaled = tag.ScaledValue.Value;
                        if (tag.AlarmHigh.HasValue && scaled > tag.AlarmHigh.Value)
                        {
                            watchEntry.HasAlarm = true;
                            watchEntry.AlarmMessage = $"HIGH ALARM: {scaled:F2} > {tag.AlarmHigh.Value:F2}";
                        }
                        else if (tag.AlarmLow.HasValue && scaled < tag.AlarmLow.Value)
                        {
                            watchEntry.HasAlarm = true;
                            watchEntry.AlarmMessage = $"LOW ALARM: {scaled:F2} < {tag.AlarmLow.Value:F2}";
                        }
                        else
                        {
                            watchEntry.HasAlarm = false;
                            watchEntry.AlarmMessage = "";
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Create a new tag group
        /// </summary>
        public async Task<TagGroup> CreateGroup(string name, string? parentGroup = null)
        {
            var group = new TagGroup
            {
                Name = name,
                ParentGroup = parentGroup ?? ""
            };

            if (!string.IsNullOrEmpty(parentGroup))
            {
                var parent = FindGroupByName(parentGroup);
                if (parent != null)
                {
                    group.ParentGroupId = parent.Id;
                    parent.SubGroups.Add(group);
                }
                else
                {
                    Groups.Add(group);
                }
            }
            else
            {
                Groups.Add(group);
            }

            await SaveTagsAsync();
            return group;
        }

        /// <summary>
        /// Rename a group. Updates display name only; GroupId-based references remain stable.
        /// </summary>
        public async Task RenameGroup(string groupId, string newName)
        {
            var group = FindGroupById(groupId);
            if (group == null)
                throw new ArgumentException($"Group with ID '{groupId}' not found.");

            var oldName = group.Name;
            group.Name = newName;

            // Update legacy Group property on tags pointing to this group
            foreach (var tag in Tags.Where(t => t.GroupId == groupId))
            {
                tag.Group = newName;
            }

            // Update legacy ParentGroup property on child groups
            foreach (var child in GetAllGroupsFlat().Where(g => g.ParentGroupId == groupId))
            {
                child.ParentGroup = newName;
            }

            _tagLogger.LogInformation("Renamed group '{OldName}' to '{NewName}' (ID: {GroupId})", oldName, newName, groupId);

            await SaveTagsAsync();
        }

        /// <summary>
        /// Get all tags in a group (including subgroups) by group ID.
        /// </summary>
        public IEnumerable<Tag> GetTagsInGroupById(string groupId)
        {
            var group = FindGroupById(groupId);
            if (group == null) return Enumerable.Empty<Tag>();

            var tags = new List<Tag>(Tags.Where(t => t.GroupId == groupId));
            foreach (var sub in group.SubGroups)
                tags.AddRange(GetTagsInGroupById(sub.Id));

            return tags;
        }

        /// <summary>
        /// Get all tags in a group (including subgroups)
        /// </summary>
        public IEnumerable<Tag> GetTagsInGroup(string groupName)
        {
            var group = FindGroupByName(groupName);
            if (group == null) return Enumerable.Empty<Tag>();

            var tags = new List<Tag>(group.Tags);
            foreach (var sub in group.SubGroups)
                tags.AddRange(GetTagsInGroupRecursive(sub));

            return tags;
        }

        private IEnumerable<Tag> GetTagsInGroupRecursive(TagGroup group)
        {
            var tags = new List<Tag>(group.Tags);
            foreach (var sub in group.SubGroups)
                tags.AddRange(GetTagsInGroupRecursive(sub));
            return tags;
        }

        /// <summary>
        /// Find a group by its stable ID across all levels.
        /// </summary>
        internal TagGroup? FindGroupById(string groupId)
        {
            return GetAllGroupsFlat().FirstOrDefault(g => g.Id == groupId);
        }

        /// <summary>
        /// Find a group by name across all levels (first match wins).
        /// </summary>
        internal TagGroup? FindGroupByName(string name)
        {
            return GetAllGroupsFlat().FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns a flat enumeration of all groups including nested subgroups.
        /// </summary>
        internal IEnumerable<TagGroup> GetAllGroupsFlat()
        {
            foreach (var group in Groups)
            {
                yield return group;
                foreach (var sub in FlattenSubGroups(group))
                    yield return sub;
            }
        }

        private static IEnumerable<TagGroup> FlattenSubGroups(TagGroup parent)
        {
            foreach (var sub in parent.SubGroups)
            {
                yield return sub;
                foreach (var nested in FlattenSubGroups(sub))
                    yield return nested;
            }
        }

        private TagGroup EnsureGroupExists(string groupName)
        {
            var existing = FindGroupByName(groupName);
            if (existing != null)
                return existing;

            var newGroup = new TagGroup { Name = groupName };
            Groups.Add(newGroup);
            return newGroup;
        }

        private async Task LoadTagsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_tagsFilePath))
                    return;

                var json = await File.ReadAllTextAsync(_tagsFilePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                // Detect schema version
                int schemaVersion = DetectSchemaVersion(json);

                var data = JsonSerializer.Deserialize<TagDatabase>(json, _jsonOptions);
                if (data == null)
                    return;

                if (schemaVersion < 2)
                {
                    _tagLogger.LogInformation("Migrating tag database from schema v{OldVersion} to v2", schemaVersion);
                    MigrateFromV1(data);
                }

                Tags = new ObservableCollection<Tag>(data.Tags);
                Groups = new ObservableCollection<TagGroup>(data.Groups);

                // Rebuild SubGroups hierarchy and Tags collections from flat data
                RebuildHierarchy();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _tagLogger.LogError(ex, "Failed to load tags from {TagsFilePath}", _tagsFilePath);
                // If load fails, start with empty database
            }
        }

        /// <summary>
        /// Detects the schema version from raw JSON. Returns 1 if no schemaVersion field found.
        /// </summary>
        private static int DetectSchemaVersion(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("schemaVersion", out var versionElement))
                {
                    return versionElement.GetInt32();
                }
            }
            catch
            {
                // Fall through to default
            }
            return 1;
        }

        /// <summary>
        /// Migrates v1 data (name-based references) to v2 (ID-based references).
        /// </summary>
        internal void MigrateFromV1(TagDatabase data)
        {
            // Build a lookup from group name to group (first match wins for duplicates)
            var groupByName = new Dictionary<string, TagGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in data.Groups)
            {
                if (!groupByName.ContainsKey(group.Name))
                {
                    groupByName[group.Name] = group;
                }
                else
                {
                    _tagLogger.LogWarning(
                        "Duplicate group name '{GroupName}' found during migration. Using first occurrence (ID: {GroupId})",
                        group.Name, groupByName[group.Name].Id);
                }
            }

            // Assign ParentGroupId from ParentGroup name (snapshot to allow adding fallbacks)
            var fallbackGroups = new List<TagGroup>();
            foreach (var group in data.Groups.ToList())
            {
                if (!string.IsNullOrEmpty(group.ParentGroup))
                {
                    if (groupByName.TryGetValue(group.ParentGroup, out var parent))
                    {
                        group.ParentGroupId = parent.Id;
                    }
                    else
                    {
                        _tagLogger.LogWarning(
                            "Group '{GroupName}' references missing parent '{ParentGroup}'. Creating fallback group.",
                            group.Name, group.ParentGroup);
                        var fallback = new TagGroup { Name = group.ParentGroup };
                        fallbackGroups.Add(fallback);
                        groupByName[group.ParentGroup] = fallback;
                        group.ParentGroupId = fallback.Id;
                    }
                }
            }

            // Add fallback parent groups
            data.Groups.AddRange(fallbackGroups);

            // Assign GroupId from Group name on tags
            var tagFallbackGroups = new List<TagGroup>();
            foreach (var tag in data.Tags)
            {
                if (!string.IsNullOrEmpty(tag.Group))
                {
                    if (groupByName.TryGetValue(tag.Group, out var group))
                    {
                        tag.GroupId = group.Id;
                    }
                    else
                    {
                        _tagLogger.LogWarning(
                            "Tag '{TagName}' references missing group '{GroupName}'. Creating fallback group.",
                            tag.Name, tag.Group);
                        var fallback = new TagGroup { Name = tag.Group };
                        tagFallbackGroups.Add(fallback);
                        groupByName[tag.Group] = fallback;
                        tag.GroupId = fallback.Id;
                    }
                }
            }

            // Add fallback tag groups
            data.Groups.AddRange(tagFallbackGroups);

            data.SchemaVersion = 2;
        }

        /// <summary>
        /// Rebuilds the SubGroups hierarchy and group Tags collections from flat lists using IDs.
        /// </summary>
        private void RebuildHierarchy()
        {
            var groupLookup = new Dictionary<string, TagGroup>();
            foreach (var group in Groups)
            {
                groupLookup[group.Id] = group;
                group.SubGroups.Clear();
                group.Tags.Clear();
            }

            // Build parent-child relationships
            var rootGroups = new List<TagGroup>();
            foreach (var group in Groups.ToList())
            {
                if (!string.IsNullOrEmpty(group.ParentGroupId) && groupLookup.TryGetValue(group.ParentGroupId, out var parent))
                {
                    parent.SubGroups.Add(group);
                }
                else
                {
                    rootGroups.Add(group);
                }
            }

            // Place tags into their groups
            foreach (var tag in Tags)
            {
                if (!string.IsNullOrEmpty(tag.GroupId) && groupLookup.TryGetValue(tag.GroupId, out var group))
                {
                    group.Tags.Add(tag);
                }
            }

            // Replace Groups with root-level only (children are in SubGroups)
            Groups = new ObservableCollection<TagGroup>(rootGroups);
        }

        // ----------------------------------------------------------------
        //  Group Deletion – Preview
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns a read-only preview describing what a group deletion would affect,
        /// without making any changes.  The destination shown is for MoveToParent mode.
        /// </summary>
        public GroupDeletionPreview PreviewGroupDeletion(string groupId)
        {
            var group = FindGroupById(groupId);
            if (group == null)
            {
                return new GroupDeletionPreview
                {
                    GroupId = groupId,
                    GroupName = "(not found)",
                    IsProtected = true,
                    Message = $"Group '{groupId}' was not found."
                };
            }

            var defaultGroup = GetOrFindDefaultGroup();
            bool isDefault = defaultGroup != null && group.Id == defaultGroup.Id;

            // Collect all descendant groups using IDs
            var allDescendants = GetAllGroupsFlat()
                .Where(g => !string.IsNullOrEmpty(g.ParentGroupId) && IsDescendantOf(g, groupId))
                .ToList();

            int directSubCount  = group.SubGroups.Count;
            int recursiveSubCount = allDescendants.Count;

            // Tags: count by GroupId references
            int directTagCount    = Tags.Count(t => t.GroupId == groupId);
            int recursiveTagCount = allDescendants.Sum(sub => Tags.Count(t => t.GroupId == sub.Id))
                                    + directTagCount;

            // Watch entries that would be removed under CascadeDelete
            var allAffectedGroupIds = new HashSet<string> { groupId };
            foreach (var d in allDescendants)
                allAffectedGroupIds.Add(d.Id);

            var affectedTagIds = Tags
                .Where(t => !string.IsNullOrEmpty(t.GroupId) && allAffectedGroupIds.Contains(t.GroupId!))
                .Select(t => t.Id)
                .ToHashSet();

            int watchToRemove = WatchEntries.Count(w => affectedTagIds.Contains(w.TagId));

            // Destination for MoveToParent
            TagGroup? destinationGroup = null;
            if (!string.IsNullOrEmpty(group.ParentGroupId))
            {
                destinationGroup = FindGroupById(group.ParentGroupId);
            }
            destinationGroup ??= defaultGroup;

            return new GroupDeletionPreview
            {
                GroupId                = group.Id,
                GroupName              = group.Name,
                FullPath               = group.FullPath,
                DirectSubgroupCount    = directSubCount,
                RecursiveSubgroupCount = recursiveSubCount,
                DirectTagCount         = directTagCount,
                RecursiveTagCount      = recursiveTagCount,
                WatchEntriesToRemove   = watchToRemove,
                DestinationGroupId     = destinationGroup?.Id   ?? string.Empty,
                DestinationGroupName   = destinationGroup?.Name ?? "Default",
                IsProtected            = isDefault
            };
        }

        // ----------------------------------------------------------------
        //  Group Deletion – Atomic Operation
        // ----------------------------------------------------------------

        /// <summary>
        /// Atomically deletes a group.  Tags and sub-groups are handled according to
        /// <paramref name="mode"/>.  Rolls back in-memory state if persistence fails.
        /// </summary>
        public async Task<GroupDeletionResult> DeleteGroupAsync(
            string groupId,
            GroupDeletionMode mode,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Fail("Operation was cancelled before it started.");

            var group = FindGroupById(groupId);
            if (group == null)
                return Fail($"Group '{groupId}' was not found.");

            var defaultGroup = GetOrFindDefaultGroup();
            if (defaultGroup != null && group.Id == defaultGroup.Id)
                return Fail("The Default group cannot be deleted.");

            // Check again via preview so the IsProtected path is consistent
            var preview = PreviewGroupDeletion(groupId);
            if (preview.IsProtected)
                return Fail($"Group '{group.Name}' is protected and cannot be deleted.");

            // Resolve the parent group that will receive moved content (for MoveToParent)
            TagGroup? parentGroup = null;
            if (!string.IsNullOrEmpty(group.ParentGroupId))
                parentGroup = FindGroupById(group.ParentGroupId);
            parentGroup ??= defaultGroup;

            // Destination for move modes
            TagGroup? destinationGroup = mode switch
            {
                GroupDeletionMode.MoveToParent  => parentGroup,
                GroupDeletionMode.MoveToDefault => defaultGroup,
                _                               => null   // CascadeDelete – no destination
            };

            // Collect all descendant groups (bottom-up to remove children first when cascading)
            var allDescendants = GetAllGroupsFlat()
                .Where(g => IsDescendantOf(g, groupId))
                .ToList();

            var allAffectedGroupIds = new HashSet<string> { groupId };
            foreach (var d in allDescendants)
                allAffectedGroupIds.Add(d.Id);

            var affectedTags = Tags
                .Where(t => !string.IsNullOrEmpty(t.GroupId) && allAffectedGroupIds.Contains(t.GroupId!))
                .ToList();

            var affectedTagIds = affectedTags.Select(t => t.Id).ToHashSet();

            var affectedWatchEntries = WatchEntries
                .Where(w => affectedTagIds.Contains(w.TagId))
                .ToList();

            // ---- Snapshot for rollback ----
            // Snapshots: parent's SubGroups list, tags collection, watch entries, group Tags
            var snapshotTags        = Tags.ToList();
            var snapshotGroups      = GetAllGroupsFlat().ToList();
            var snapshotWatchEntries = WatchEntries.ToList();

            // Per-group Tags-collection snapshots (needed to restore group.Tags on rollback)
            var groupTagsSnapshot = GetAllGroupsFlat()
                .ToDictionary(g => g.Id, g => g.Tags.ToList());
            var groupSubsSnapshot = GetAllGroupsFlat()
                .ToDictionary(g => g.Id, g => g.SubGroups.ToList());

            // ---- Apply mutation ----
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return Fail("Operation was cancelled.");

                int movedTagCount   = 0;
                int deletedTagCount = 0;
                int removedWatchCount = 0;

                if (mode == GroupDeletionMode.CascadeDelete)
                {
                    // Remove all affected tags from flat Tags list
                    foreach (var tag in affectedTags)
                        Tags.Remove(tag);

                    // Remove watch entries for deleted tags
                    foreach (var we in affectedWatchEntries)
                        WatchEntries.Remove(we);

                    deletedTagCount  = affectedTags.Count;
                    removedWatchCount = affectedWatchEntries.Count;
                }
                else
                {
                    // Move mode: re-parent tags and sub-groups
                    if (destinationGroup == null)
                        return Fail("Could not resolve destination group for move.");

                    // Move direct tags of the deleted group
                    foreach (var tag in affectedTags.Where(t => t.GroupId == groupId).ToList())
                    {
                        tag.GroupId = destinationGroup.Id;
                        tag.Group   = destinationGroup.Name;
                        group.Tags.Remove(tag);
                        destinationGroup.Tags.Add(tag);
                        movedTagCount++;
                    }

                    // Move direct sub-groups of the deleted group
                    foreach (var sub in group.SubGroups.ToList())
                    {
                        sub.ParentGroupId = destinationGroup.Id;
                        sub.ParentGroup   = destinationGroup.Name;
                        group.SubGroups.Remove(sub);
                        destinationGroup.SubGroups.Add(sub);
                    }

                    // For tags in deeper descendants: reparent them to the destination group as well
                    var deepTags = affectedTags
                        .Where(t => t.GroupId != groupId)
                        .ToList();
                    foreach (var tag in deepTags)
                    {
                        var oldGroup = FindGroupById(tag.GroupId!);
                        // These tags stay in their own subgroup (which moved up) – no relocation needed
                        // unless MoveToDefault flattens all levels.
                        // Under MoveToParent the descendants also moved up so tags remain correct.
                        movedTagCount++;
                    }

                    // Update watch entries to reflect new group name (TagGroup field is display only)
                    foreach (var we in affectedWatchEntries)
                    {
                        var relocatedTag = Tags.FirstOrDefault(t => t.Id == we.TagId);
                        if (relocatedTag != null)
                            we.TagGroup = relocatedTag.Group;
                    }
                }

                // Remove the target group from its parent's SubGroups or from root Groups
                RemoveGroupFromParent(group);

                // Also remove all descendant groups from the flat registry
                // (they live only inside SubGroups hierarchies so after RemoveGroupFromParent
                //  the whole subtree disappears from GetAllGroupsFlat automatically;
                //  but under cascade we must also remove any whose tags were removed).
                // Nothing extra needed – the subtree is gone via the hierarchy.

                // ---- Persist ----
                await SaveTagsAsync();

                _tagLogger.LogInformation(
                    "Deleted group '{GroupName}' (ID: {GroupId}) Mode={Mode}. " +
                    "Moved={MovedTags}, Deleted={DeletedTags}, RemovedWatch={RemovedWatch}",
                    group.Name, groupId, mode, movedTagCount, deletedTagCount, removedWatchCount);

                return new GroupDeletionResult
                {
                    Success               = true,
                    Message               = $"Group '{group.Name}' deleted successfully.",
                    DeletedGroupCount     = 1 + allDescendants.Count,
                    MovedTagCount         = movedTagCount,
                    DeletedTagCount       = deletedTagCount,
                    RemovedWatchEntryCount = removedWatchCount
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // ---- Rollback ----
                _tagLogger.LogError(ex, "DeleteGroupAsync failed for '{GroupId}'; rolling back.", groupId);
                RollbackDeletion(snapshotTags, snapshotGroups, snapshotWatchEntries,
                                 groupTagsSnapshot, groupSubsSnapshot);
                return Fail($"Deletion failed and was rolled back: {ex.Message}");
            }
        }

        // ---- Helpers ----

        private static GroupDeletionResult Fail(string message) =>
            new() { Success = false, Message = message };

        /// <summary>Returns true if <paramref name="group"/> is a descendant of the group
        /// with ID <paramref name="ancestorId"/>.</summary>
        private bool IsDescendantOf(TagGroup group, string ancestorId)
        {
            var current = group;
            var visited = new HashSet<string>();
            while (!string.IsNullOrEmpty(current.ParentGroupId))
            {
                if (!visited.Add(current.Id))
                    break; // Cycle guard
                if (current.ParentGroupId == ancestorId)
                    return true;
                var parent = FindGroupById(current.ParentGroupId);
                if (parent == null)
                    break;
                current = parent;
            }
            return false;
        }

        /// <summary>
        /// Finds the top-level Default group (first group named "Default" at root level).
        /// </summary>
        private TagGroup? GetOrFindDefaultGroup()
        {
            return Groups.FirstOrDefault(g => g.Name.Equals("Default", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Removes <paramref name="group"/> from its parent's SubGroups, or from the root
        /// Groups collection if it has no parent.
        /// </summary>
        private void RemoveGroupFromParent(TagGroup group)
        {
            if (!string.IsNullOrEmpty(group.ParentGroupId))
            {
                var parent = FindGroupById(group.ParentGroupId);
                parent?.SubGroups.Remove(group);
            }
            else
            {
                Groups.Remove(group);
            }
        }

        /// <summary>
        /// Restores all in-memory collections to their pre-deletion snapshots.
        /// </summary>
        private void RollbackDeletion(
            List<Tag> snapshotTags,
            List<TagGroup> snapshotGroups,
            List<WatchEntry> snapshotWatchEntries,
            Dictionary<string, List<Tag>> groupTagsSnapshot,
            Dictionary<string, List<TagGroup>> groupSubsSnapshot)
        {
            // Restore flat tags list
            Tags.Clear();
            foreach (var t in snapshotTags) Tags.Add(t);

            // Restore watch entries
            WatchEntries.Clear();
            foreach (var w in snapshotWatchEntries) WatchEntries.Add(w);

            // Restore group SubGroups and Tags collections from snapshots
            foreach (var group in snapshotGroups)
            {
                if (groupTagsSnapshot.TryGetValue(group.Id, out var tags))
                {
                    group.Tags.Clear();
                    foreach (var t in tags) group.Tags.Add(t);
                }
                if (groupSubsSnapshot.TryGetValue(group.Id, out var subs))
                {
                    group.SubGroups.Clear();
                    foreach (var s in subs) group.SubGroups.Add(s);
                }
            }

            // Restore root Groups collection (rebuild from snapshot root-level groups)
            var snapshotRoots = snapshotGroups
                .Where(g => string.IsNullOrEmpty(g.ParentGroupId))
                .ToList();
            Groups.Clear();
            foreach (var g in snapshotRoots) Groups.Add(g);
        }

        // ----------------------------------------------------------------
        //  Preview helper (declared here, shared with GroupDeletionPreview)
        // ----------------------------------------------------------------

        /// <summary>
        /// Workaround property accessor for GroupDeletionPreview.Message —
        /// available only on the preview object, declared here as a private static extension point.
        /// </summary>
        private static GroupDeletionPreview ErrorPreview(string groupId, string message) =>
            new()
            {
                GroupId     = groupId,
                IsProtected = true,
                Message     = message
            };

        /// <summary>
        /// Saves the tag database atomically (write to temp file, then replace).
        /// Throws on failure so callers can handle it.
        /// </summary>
        internal async Task SaveTagsAsync()
        {
            var directory = Path.GetDirectoryName(_tagsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Collect all groups (flattened) for persistence
            var allGroups = GetAllGroupsFlat().ToList();

            var data = new TagDatabase
            {
                SchemaVersion = 2,
                Tags = Tags.ToList(),
                Groups = allGroups
            };

            var json = JsonSerializer.Serialize(data, _jsonOptions);

            // Atomic write: write to temp file, flush, then replace
            var tempFilePath = _tagsFilePath + ".tmp";
            try
            {
                await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(json);
                    await writer.FlushAsync();
                    await stream.FlushAsync();
                }

                // Atomic replace
                File.Move(tempFilePath, _tagsFilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _tagLogger.LogError(ex, "Failed to save tags to {TagsFilePath}", _tagsFilePath);

                // Clean up temp file if it exists
                try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { /* best effort */ }

                throw; // Surface failure to callers
            }
        }
    }
}

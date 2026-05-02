using CommunityToolkit.Mvvm.ComponentModel;
using ModbusForge.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Service for managing the tag database - symbolic addressing for Modbus registers
    /// </summary>
    public partial class TagService : ObservableObject
    {
        private readonly string _tagsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        [ObservableProperty]
        private ObservableCollection<Tag> _tags = new();

        [ObservableProperty]
        private ObservableCollection<TagGroup> _groups = new();

        [ObservableProperty]
        private ObservableCollection<WatchEntry> _watchEntries = new();

        public TagService()
        {
            _tagsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ModbusForge",
                "tags.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Create default group
            _groups.Add(new TagGroup { Name = "Default", Description = "Default tag group" });

            // Load existing tags
            LoadTagsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Create a new tag with symbolic name
        /// </summary>
        public Tag CreateTag(string name, string group, PlcArea area, int address, TagDataType dataType)
        {
            var tag = new Tag
            {
                Name = name,
                Group = group,
                Area = area,
                Address = address,
                DataType = dataType
            };

            Tags.Add(tag);
            
            // Ensure group exists
            EnsureGroupExists(group);
            
            SaveTagsAsync().ConfigureAwait(false);
            
            return tag;
        }

        /// <summary>
        /// Delete a tag by ID
        /// </summary>
        public void DeleteTag(string tagId)
        {
            var tag = Tags.FirstOrDefault(t => t.Id == tagId);
            if (tag != null)
            {
                Tags.Remove(tag);
                
                // Remove from watch entries too
                var watchEntry = WatchEntries.FirstOrDefault(w => w.TagId == tagId);
                if (watchEntry != null)
                    WatchEntries.Remove(watchEntry);
                
                SaveTagsAsync().ConfigureAwait(false);
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
        public TagGroup CreateGroup(string name, string? parentGroup = null)
        {
            var group = new TagGroup
            {
                Name = name,
                ParentGroup = parentGroup ?? ""
            };

            if (!string.IsNullOrEmpty(parentGroup))
            {
                var parent = Groups.FirstOrDefault(g => g.Name == parentGroup);
                if (parent != null)
                    parent.SubGroups.Add(group);
                else
                    Groups.Add(group);
            }
            else
            {
                Groups.Add(group);
            }

            SaveTagsAsync().ConfigureAwait(false);
            return group;
        }

        /// <summary>
        /// Get all tags in a group (including subgroups)
        /// </summary>
        public IEnumerable<Tag> GetTagsInGroup(string groupName)
        {
            var group = Groups.FirstOrDefault(g => g.Name == groupName);
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

        private void EnsureGroupExists(string groupName)
        {
            if (!Groups.Any(g => g.Name == groupName))
            {
                Groups.Add(new TagGroup { Name = groupName });
            }
        }

        private async Task LoadTagsAsync()
        {
            try
            {
                if (File.Exists(_tagsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_tagsFilePath);
                    var data = JsonSerializer.Deserialize<TagDatabase>(json, _jsonOptions);
                    
                    if (data != null)
                    {
                        Tags = new ObservableCollection<Tag>(data.Tags);
                        Groups = new ObservableCollection<TagGroup>(data.Groups);
                    }
                }
            }
            catch (Exception)
            {
                // If load fails, start with empty database
            }
        }

        private async Task SaveTagsAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_tagsFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var data = new TagDatabase
                {
                    Tags = Tags.ToList(),
                    Groups = Groups.ToList()
                };

                var json = JsonSerializer.Serialize(data, _jsonOptions);
                await File.WriteAllTextAsync(_tagsFilePath, json);
            }
            catch (Exception)
            {
                // Silent fail - will retry on next save
            }
        }

        private class TagDatabase
        {
            public List<Tag> Tags { get; set; } = new();
            public List<TagGroup> Groups { get; set; } = new();
        }
    }
}

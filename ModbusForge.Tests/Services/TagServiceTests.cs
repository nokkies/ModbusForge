using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class TagServiceTests : IDisposable
    {
        private static readonly FieldInfo TagsFilePathField = typeof(TagService).GetField(
            "_tagsFilePath",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly List<string> _tempDirs = new();

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch { /* best effort cleanup */ }
            }
        }

        private TagService CreateService(out string tagsFilePath, out Mock<ILogger<Tag>> mockLogger)
        {
            mockLogger = new Mock<ILogger<Tag>>();
            var service = new TagService(mockLogger.Object);

            var tempDir = Path.Combine(Path.GetTempPath(), "ModbusForgeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);
            tagsFilePath = Path.Combine(tempDir, "tags.json");

            TagsFilePathField.SetValue(service, tagsFilePath);
            return service;
        }

        private async Task<(TagService service, string tagsFilePath, Mock<ILogger<Tag>> mockLogger)> CreateServiceWithFileAsync(string json)
        {
            var mockLogger = new Mock<ILogger<Tag>>();
            var service = new TagService(mockLogger.Object);

            var tempDir = Path.Combine(Path.GetTempPath(), "ModbusForgeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);
            var tagsFilePath = Path.Combine(tempDir, "tags.json");

            TagsFilePathField.SetValue(service, tagsFilePath);
            await File.WriteAllTextAsync(tagsFilePath, json);
            await service.InitializeAsync();
            return (service, tagsFilePath, mockLogger);
        }

        #region Legacy JSON Migration (v1 -> v2)

        [Fact]
        public async Task LegacyMigration_V1ToV2_AssignsGroupIds()
        {
            // v1 schema: no schemaVersion, tags use Group name, groups use ParentGroup name
            var v1Json = JsonSerializer.Serialize(new
            {
                tags = new[]
                {
                    new { id = "tag1", name = "Temp", group = "Sensors", groupId = (string?)null, address = 1, area = 0, dataType = 0, description = "", scale = 1.0, offset = 0.0, units = "", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false },
                    new { id = "tag2", name = "Pressure", group = "Sensors", groupId = (string?)null, address = 2, area = 0, dataType = 0, description = "", scale = 1.0, offset = 0.0, units = "", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false }
                },
                groups = new[]
                {
                    new { id = "grp1", name = "Sensors", description = "", parentGroup = "", parentGroupId = (string?)null }
                }
            }, JsonOptions);

            var (service, _, _) = await CreateServiceWithFileAsync(v1Json);

            // Tags should have GroupId assigned
            var tag1 = service.Tags.First(t => t.Id == "tag1");
            var tag2 = service.Tags.First(t => t.Id == "tag2");
            Assert.Equal("grp1", tag1.GroupId);
            Assert.Equal("grp1", tag2.GroupId);
        }

        [Fact]
        public async Task LegacyMigration_V2_DoesNotReassign()
        {
            // v2 schema already has schemaVersion and GroupId
            var v2Json = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                tags = new[]
                {
                    new { id = "tag1", name = "Temp", group = "Sensors", groupId = "grp1", address = 1, area = 0, dataType = 0, description = "", scale = 1.0, offset = 0.0, units = "", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false }
                },
                groups = new[]
                {
                    new { id = "grp1", name = "Sensors", description = "", parentGroup = "", parentGroupId = (string?)null }
                }
            }, JsonOptions);

            var (service, _, _) = await CreateServiceWithFileAsync(v2Json);

            var tag1 = service.Tags.First(t => t.Id == "tag1");
            Assert.Equal("grp1", tag1.GroupId);
        }

        #endregion

        #region Duplicate Group Names Under Different Parents

        [Fact]
        public async Task DuplicateGroupNames_UnderDifferentParents_FirstMatchWins()
        {
            // Two groups named "SubA" — one under "Parent1", one under "Parent2"
            var v1Json = JsonSerializer.Serialize(new
            {
                tags = new[]
                {
                    new { id = "tag1", name = "T1", group = "SubA", groupId = (string?)null, address = 1, area = 0, dataType = 0, description = "", scale = 1.0, offset = 0.0, units = "", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false }
                },
                groups = new[]
                {
                    new { id = "parent1", name = "Parent1", description = "", parentGroup = "", parentGroupId = (string?)null },
                    new { id = "subA1", name = "SubA", description = "", parentGroup = "Parent1", parentGroupId = (string?)null },
                    new { id = "subA2", name = "SubA", description = "", parentGroup = "Parent2", parentGroupId = (string?)null },
                    new { id = "parent2", name = "Parent2", description = "", parentGroup = "", parentGroupId = (string?)null }
                }
            }, JsonOptions);

            var (service, _, mockLogger) = await CreateServiceWithFileAsync(v1Json);

            // Tag should be assigned to the FIRST "SubA" (subA1)
            var tag1 = service.Tags.First(t => t.Id == "tag1");
            Assert.Equal("subA1", tag1.GroupId);

            // Logger should have warned about duplicate
            mockLogger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Duplicate group name")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        #endregion

        #region Missing Group References Create Fallback

        [Fact]
        public async Task MissingGroupReference_CreatesFallbackGroup()
        {
            var v1Json = JsonSerializer.Serialize(new
            {
                tags = new[]
                {
                    new { id = "tag1", name = "Orphan", group = "NonExistent", groupId = (string?)null, address = 1, area = 0, dataType = 0, description = "", scale = 1.0, offset = 0.0, units = "", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false }
                },
                groups = new[]
                {
                    new { id = "grp1", name = "Default", description = "", parentGroup = "", parentGroupId = (string?)null }
                }
            }, JsonOptions);

            var (service, _, _) = await CreateServiceWithFileAsync(v1Json);

            var tag1 = service.Tags.First(t => t.Id == "tag1");
            Assert.NotNull(tag1.GroupId);
            Assert.NotEmpty(tag1.GroupId!);

            // The fallback group "NonExistent" should exist
            var fallbackGroup = service.GetAllGroupsFlat().FirstOrDefault(g => g.Name == "NonExistent");
            Assert.NotNull(fallbackGroup);
            Assert.Equal(tag1.GroupId, fallbackGroup!.Id);
        }

        [Fact]
        public async Task MissingParentGroupReference_CreatesFallbackParentGroup()
        {
            var v1Json = JsonSerializer.Serialize(new
            {
                tags = Array.Empty<object>(),
                groups = new[]
                {
                    new { id = "child1", name = "ChildGroup", description = "", parentGroup = "MissingParent", parentGroupId = (string?)null }
                }
            }, JsonOptions);

            var (service, _, _) = await CreateServiceWithFileAsync(v1Json);

            var child = service.GetAllGroupsFlat().First(g => g.Id == "child1");
            Assert.NotNull(child.ParentGroupId);

            // "MissingParent" should be created as a fallback
            var fallback = service.GetAllGroupsFlat().FirstOrDefault(g => g.Name == "MissingParent");
            Assert.NotNull(fallback);
            Assert.Equal(child.ParentGroupId, fallback!.Id);
        }

        #endregion

        #region Nested Group Reconstruction

        [Fact]
        public async Task NestedGroups_ReconstructedCorrectly()
        {
            var v2Json = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                tags = new[]
                {
                    new { id = "tag1", name = "T1", group = "Child", groupId = "child1", address = 1, area = 0, dataType = 0, description = "", scale = 1.0, offset = 0.0, units = "", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false }
                },
                groups = new[]
                {
                    new { id = "root1", name = "Root", description = "", parentGroup = "", parentGroupId = (string?)null },
                    new { id = "child1", name = "Child", description = "", parentGroup = "Root", parentGroupId = "root1" },
                    new { id = "grandchild1", name = "GrandChild", description = "", parentGroup = "Child", parentGroupId = "child1" }
                }
            }, JsonOptions);

            var (service, _, _) = await CreateServiceWithFileAsync(v2Json);

            // Root-level Groups should contain only "Root"
            Assert.Single(service.Groups.Where(g => g.Name == "Root"));
            Assert.DoesNotContain(service.Groups, g => g.Name == "Child");
            Assert.DoesNotContain(service.Groups, g => g.Name == "GrandChild");

            // Root should have Child as sub-group
            var root = service.Groups.First(g => g.Name == "Root");
            Assert.Single(root.SubGroups);
            Assert.Equal("Child", root.SubGroups[0].Name);

            // Child should have GrandChild as sub-group
            var child = root.SubGroups[0];
            Assert.Single(child.SubGroups);
            Assert.Equal("GrandChild", child.SubGroups[0].Name);

            // Tag should be in Child's Tags collection
            Assert.Single(child.Tags);
            Assert.Equal("tag1", child.Tags[0].Id);
        }

        #endregion

        #region Group Rename Preserves Tag Membership

        [Fact]
        public async Task GroupRename_PreservesTagMembership()
        {
            var service = CreateService(out var tagsFilePath, out _);

            // Create group and tag
            var group = await service.CreateGroup("OriginalName");
            var tag = await service.CreateTag("MyTag", "OriginalName", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

            Assert.Equal(group.Id, tag.GroupId);

            // Rename group
            await service.RenameGroup(group.Id, "RenamedGroup");

            // Tag still belongs to the same group (by ID)
            Assert.Equal(group.Id, tag.GroupId);
            // Legacy Group property updated
            Assert.Equal("RenamedGroup", tag.Group);
            // Group name changed
            Assert.Equal("RenamedGroup", group.Name);
        }

        #endregion

        #region Round-Trip Serialization of GroupId/ParentGroupId

        [Fact]
        public async Task RoundTrip_GroupIdAndParentGroupId_Preserved()
        {
            var service = CreateService(out var tagsFilePath, out _);

            var parent = await service.CreateGroup("ParentGroup");
            var child = await service.CreateGroup("ChildGroup", "ParentGroup");
            var tag = await service.CreateTag("TestTag", "ChildGroup", PlcArea.HoldingRegister, 10, TagDataType.Float);

            // Verify IDs set
            Assert.Equal(child.Id, tag.GroupId);
            Assert.Equal(parent.Id, child.ParentGroupId);

            // Read back from file
            var service2 = CreateService(out var _, out _);
            TagsFilePathField.SetValue(service2, tagsFilePath);
            await service2.InitializeAsync();

            var loadedTag = service2.Tags.First(t => t.Id == tag.Id);
            Assert.Equal(child.Id, loadedTag.GroupId);

            var loadedChild = service2.GetAllGroupsFlat().First(g => g.Id == child.Id);
            Assert.Equal(parent.Id, loadedChild.ParentGroupId);
        }

        #endregion

        #region Failed Save Does Not Corrupt Existing File

        [Fact]
        public async Task FailedSave_DoesNotCorruptExistingFile()
        {
            var service = CreateService(out var tagsFilePath, out _);

            // Create initial data and save
            await service.CreateTag("InitialTag", "Default", PlcArea.HoldingRegister, 1, TagDataType.UInt16);
            Assert.True(File.Exists(tagsFilePath));
            var originalContent = await File.ReadAllTextAsync(tagsFilePath);

            // Make the file path point to a read-only directory to simulate failure
            var readOnlyDir = Path.Combine(Path.GetTempPath(), "ModbusForgeTests_RO_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(readOnlyDir);
            _tempDirs.Add(readOnlyDir);
            var readOnlyFile = Path.Combine(readOnlyDir, "tags.json");
            await File.WriteAllTextAsync(readOnlyFile, originalContent);

            // Make the temp file path unwritable by creating a directory with that name
            var tempFilePath = readOnlyFile + ".tmp";
            Directory.CreateDirectory(tempFilePath); // This will cause File.Create to fail

            TagsFilePathField.SetValue(service, readOnlyFile);

            // Attempt to save - should throw (IOException or UnauthorizedAccessException)
            var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await service.CreateTag("FailTag", "Default", PlcArea.HoldingRegister, 99, TagDataType.UInt16);
            });
            Assert.True(ex is IOException or UnauthorizedAccessException,
                $"Expected IOException or UnauthorizedAccessException but got {ex.GetType().Name}");

            // Original file should be unchanged
            var afterContent = await File.ReadAllTextAsync(readOnlyFile);
            Assert.Equal(originalContent, afterContent);
        }

        #endregion

        #region InitializeAsync Returns Valid Data

        [Fact]
        public async Task InitializeAsync_ReturnsValidData_Immediately()
        {
            var v2Json = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                tags = new[]
                {
                    new { id = "tag1", name = "Ready", group = "Default", groupId = "grp1", address = 5, area = 0, dataType = 2, description = "test", scale = 1.0, offset = 0.0, units = "psi", isAlarmEnabled = false, alarmHigh = (double?)null, alarmLow = (double?)null, isReadOnly = false }
                },
                groups = new[]
                {
                    new { id = "grp1", name = "Default", description = "Default group", parentGroup = "", parentGroupId = (string?)null }
                }
            }, JsonOptions);

            var (service, _, _) = await CreateServiceWithFileAsync(v2Json);

            // Data available immediately after InitializeAsync
            Assert.Single(service.Tags);
            Assert.Equal("Ready", service.Tags[0].Name);
            Assert.Equal(5, service.Tags[0].Address);
            Assert.Equal("psi", service.Tags[0].Units);

            // Group hierarchy correct
            Assert.Single(service.Groups);
            Assert.Equal("Default", service.Groups[0].Name);
            Assert.Single(service.Groups[0].Tags);
        }

        [Fact]
        public async Task InitializeAsync_MultipleCalls_Idempotent()
        {
            var service = CreateService(out var tagsFilePath, out _);
            await service.CreateTag("T1", "Default", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

            // Reset and re-initialize from file
            var service2 = CreateService(out _, out _);
            TagsFilePathField.SetValue(service2, tagsFilePath);

            await service2.InitializeAsync();
            await service2.InitializeAsync(); // Second call should be no-op

            Assert.Single(service2.Tags);
        }

        #endregion

        #region Existing Tests (Updated)

        [Fact]
        public async Task CreateTag_AddsTagAndPersists()
        {
            var service = CreateService(out var tagsFilePath, out _);

            var tag = await service.CreateTag("TestTag", "Default", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

            Assert.NotNull(tag);
            Assert.Equal("TestTag", tag.Name);
            Assert.NotNull(tag.GroupId);
            Assert.Contains(service.Tags, t => t.Id == tag.Id);

            Assert.True(File.Exists(tagsFilePath));
            var json = await File.ReadAllTextAsync(tagsFilePath);
            using var doc = JsonDocument.Parse(json);

            // Verify schema version persisted
            Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32());

            var tagNames = doc.RootElement
                .GetProperty("tags")
                .EnumerateArray()
                .Select(t => t.GetProperty("name").GetString());

            Assert.Contains("TestTag", tagNames);
        }

        [Fact]
        public async Task DeleteTag_RemovesTagAndPersists()
        {
            var service = CreateService(out var tagsFilePath, out _);

            var tag = await service.CreateTag("ToDelete", "Default", PlcArea.HoldingRegister, 2, TagDataType.UInt16);
            var tagId = tag.Id;

            await service.DeleteTag(tagId);

            Assert.DoesNotContain(service.Tags, t => t.Id == tagId);

            var json = await File.ReadAllTextAsync(tagsFilePath);
            using var doc = JsonDocument.Parse(json);
            var tagIds = doc.RootElement
                .GetProperty("tags")
                .EnumerateArray()
                .Select(t => t.GetProperty("id").GetString());

            Assert.DoesNotContain(tagId, tagIds);
        }

        [Fact]
        public async Task CreateGroup_AddsGroupAndPersists()
        {
            var service = CreateService(out var tagsFilePath, out _);

            var group = await service.CreateGroup("NewGroup");

            Assert.NotNull(group);
            Assert.Equal("NewGroup", group.Name);
            Assert.NotEmpty(group.Id);
            Assert.Contains(service.Groups, g => g.Name == "NewGroup");

            var json = await File.ReadAllTextAsync(tagsFilePath);
            using var doc = JsonDocument.Parse(json);
            var groupNames = doc.RootElement
                .GetProperty("groups")
                .EnumerateArray()
                .Select(g => g.GetProperty("name").GetString());

            Assert.Contains("NewGroup", groupNames);
        }

        #endregion

        // ====================================================================
        //  Group Deletion Tests (Phase 2)
        // ====================================================================

        #region Helper – build a hierarchy

        /// <summary>
        /// Builds: Default (root), Parent, Child (under Parent), GrandChild (under Child)
        /// and creates one tag in each non-default group.
        /// Returns the created groups and tags for easy reference.
        /// </summary>
        private async Task<(TagService service,
                             TagGroup defaultGroup,
                             TagGroup parent,
                             TagGroup child,
                             TagGroup grandChild,
                             Tag parentTag,
                             Tag childTag,
                             Tag grandChildTag)>
            BuildDeepHierarchyAsync()
        {
            var service = CreateService(out _, out _);

            var defaultGroup = service.Groups.First(g => g.Name == "Default");
            var parent       = await service.CreateGroup("Parent");
            var child        = await service.CreateGroup("Child", "Parent");
            var grandChild   = await service.CreateGroup("GrandChild", "Child");

            var parentTag    = await service.CreateTag("ParentTag",    "Parent",     PlcArea.HoldingRegister, 1, TagDataType.UInt16);
            var childTag     = await service.CreateTag("ChildTag",     "Child",      PlcArea.HoldingRegister, 2, TagDataType.UInt16);
            var grandChildTag = await service.CreateTag("GrandChildTag","GrandChild", PlcArea.HoldingRegister, 3, TagDataType.UInt16);

            return (service, defaultGroup, parent, child, grandChild, parentTag, childTag, grandChildTag);
        }

        #endregion

        #region Delete empty groups

        [Fact]
        public async Task DeleteGroup_EmptyRootGroup_Succeeds()
        {
            var service = CreateService(out _, out _);
            var group = await service.CreateGroup("EmptyRoot");

            var result = await service.DeleteGroupAsync(group.Id, GroupDeletionMode.MoveToParent);

            Assert.True(result.Success);
            Assert.Equal(1, result.DeletedGroupCount);
            Assert.Null(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == group.Id));
        }

        [Fact]
        public async Task DeleteGroup_EmptyNestedGroup_Succeeds()
        {
            var service = CreateService(out _, out _);
            var parent = await service.CreateGroup("Parent");
            var child  = await service.CreateGroup("Child", "Parent");

            var result = await service.DeleteGroupAsync(child.Id, GroupDeletionMode.MoveToParent);

            Assert.True(result.Success);
            Assert.Equal(1, result.DeletedGroupCount);
            // Parent should no longer list child as sub-group
            Assert.Empty(parent.SubGroups);
        }

        #endregion

        #region Reject deletion of Default

        [Fact]
        public async Task DeleteGroup_DefaultGroup_IsRejected()
        {
            var service = CreateService(out _, out _);
            var defaultGroup = service.Groups.First(g => g.Name == "Default");

            var result = await service.DeleteGroupAsync(defaultGroup.Id, GroupDeletionMode.MoveToParent);

            Assert.False(result.Success);
            Assert.NotEmpty(result.Message);
            // Default group must still exist
            Assert.NotNull(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == defaultGroup.Id));
        }

        #endregion

        #region MoveToParent

        [Fact]
        public async Task DeleteGroup_MoveToParent_DirectTagsMovedUp()
        {
            var (service, _, parent, child, _, _, childTag, _) = await BuildDeepHierarchyAsync();

            var result = await service.DeleteGroupAsync(child.Id, GroupDeletionMode.MoveToParent);

            Assert.True(result.Success);
            Assert.True(result.MovedTagCount > 0);

            // childTag should now belong to Parent
            var reloadedTag = service.Tags.First(t => t.Id == childTag.Id);
            Assert.Equal(parent.Id, reloadedTag.GroupId);
            Assert.Equal("Parent", reloadedTag.Group);

            // child group must be gone
            Assert.Null(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == child.Id));
        }

        [Fact]
        public async Task DeleteGroup_MoveToParent_DirectSubgroupsMovedUp()
        {
            var (service, _, parent, child, grandChild, _, _, _) = await BuildDeepHierarchyAsync();

            var result = await service.DeleteGroupAsync(child.Id, GroupDeletionMode.MoveToParent);

            Assert.True(result.Success);

            // GrandChild should now be a direct subgroup of Parent
            Assert.Contains(parent.SubGroups, g => g.Id == grandChild.Id);
            Assert.Equal(parent.Id, grandChild.ParentGroupId);
        }

        #endregion

        #region MoveToDefault

        [Fact]
        public async Task DeleteGroup_MoveToDefault_TagsMovedToDefault()
        {
            var (service, defaultGroup, _, child, _, _, childTag, _) = await BuildDeepHierarchyAsync();

            var result = await service.DeleteGroupAsync(child.Id, GroupDeletionMode.MoveToDefault);

            Assert.True(result.Success);

            // childTag should now belong to Default
            var reloadedTag = service.Tags.First(t => t.Id == childTag.Id);
            Assert.Equal(defaultGroup.Id, reloadedTag.GroupId);

            // child group must be gone
            Assert.Null(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == child.Id));
        }

        [Fact]
        public async Task DeleteGroup_MoveToDefault_SubgroupsMovedToDefault()
        {
            var (service, defaultGroup, _, child, grandChild, _, _, _) = await BuildDeepHierarchyAsync();

            var result = await service.DeleteGroupAsync(child.Id, GroupDeletionMode.MoveToDefault);

            Assert.True(result.Success);

            // GrandChild should now be a direct subgroup of Default
            Assert.Contains(defaultGroup.SubGroups, g => g.Id == grandChild.Id);
            Assert.Equal(defaultGroup.Id, grandChild.ParentGroupId);
        }

        #endregion

        #region CascadeDelete

        [Fact]
        public async Task DeleteGroup_CascadeDelete_RemovesAllDescendantGroups()
        {
            var (service, _, parent, child, grandChild, _, _, _) = await BuildDeepHierarchyAsync();

            var result = await service.DeleteGroupAsync(parent.Id, GroupDeletionMode.CascadeDelete);

            Assert.True(result.Success);
            // parent, child, grandChild all gone
            Assert.Null(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == parent.Id));
            Assert.Null(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == child.Id));
            Assert.Null(service.GetAllGroupsFlat().FirstOrDefault(g => g.Id == grandChild.Id));
        }

        [Fact]
        public async Task DeleteGroup_CascadeDelete_RemovesAllDescendantTags()
        {
            var (service, _, parent, _, _, parentTag, childTag, grandChildTag) = await BuildDeepHierarchyAsync();

            var result = await service.DeleteGroupAsync(parent.Id, GroupDeletionMode.CascadeDelete);

            Assert.True(result.Success);
            Assert.Equal(3, result.DeletedTagCount);  // parentTag + childTag + grandChildTag

            Assert.Null(service.Tags.FirstOrDefault(t => t.Id == parentTag.Id));
            Assert.Null(service.Tags.FirstOrDefault(t => t.Id == childTag.Id));
            Assert.Null(service.Tags.FirstOrDefault(t => t.Id == grandChildTag.Id));
        }

        [Fact]
        public async Task DeleteGroup_CascadeDelete_RemovesWatchEntries()
        {
            var (service, _, parent, _, _, parentTag, childTag, grandChildTag) = await BuildDeepHierarchyAsync();

            // Add all tags to watch
            service.AddToWatch(parentTag.Id);
            service.AddToWatch(childTag.Id);
            service.AddToWatch(grandChildTag.Id);
            Assert.Equal(3, service.WatchEntries.Count);

            var result = await service.DeleteGroupAsync(parent.Id, GroupDeletionMode.CascadeDelete);

            Assert.True(result.Success);
            Assert.Equal(3, result.RemovedWatchEntryCount);
            Assert.Empty(service.WatchEntries);
        }

        #endregion

        #region Watch entries preserved on move

        [Fact]
        public async Task DeleteGroup_MoveToParent_PreservesWatchEntries()
        {
            var (service, _, _, child, _, _, childTag, _) = await BuildDeepHierarchyAsync();

            service.AddToWatch(childTag.Id);
            Assert.Single(service.WatchEntries);

            var result = await service.DeleteGroupAsync(child.Id, GroupDeletionMode.MoveToParent);

            Assert.True(result.Success);
            // Watch entry should still exist (tag was moved, not deleted)
            Assert.Single(service.WatchEntries);
            Assert.Equal(childTag.Id, service.WatchEntries[0].TagId);
        }

        #endregion

        #region Persistence: no dangling group IDs after deletion

        [Fact]
        public async Task DeleteGroup_Persistence_NoDanglingGroupIds()
        {
            var service = CreateService(out var tagsFilePath, out _);
            var parent  = await service.CreateGroup("ToDeleteGroup");
            var child   = await service.CreateGroup("ChildOfToDelete", "ToDeleteGroup");
            await service.CreateTag("T1", "ChildOfToDelete", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

            await service.DeleteGroupAsync(parent.Id, GroupDeletionMode.CascadeDelete);

            // Reload and verify
            var service2 = CreateService(out _, out _);
            TagsFilePathField.SetValue(service2, tagsFilePath);
            await service2.InitializeAsync();

            var allGroupIds = service2.GetAllGroupsFlat().Select(g => g.Id).ToHashSet();

            // No tag should reference a non-existent group
            foreach (var tag in service2.Tags)
            {
                if (!string.IsNullOrEmpty(tag.GroupId))
                    Assert.Contains(tag.GroupId, allGroupIds);
            }

            // Neither parent nor child group should be in the file
            Assert.Null(service2.GetAllGroupsFlat().FirstOrDefault(g => g.Id == parent.Id));
            Assert.Null(service2.GetAllGroupsFlat().FirstOrDefault(g => g.Id == child.Id));
        }

        #endregion

        #region Rollback on save failure

        [Fact]
        public async Task DeleteGroup_SaveFailure_RollsBackInMemoryChanges()
        {
            var service = CreateService(out var tagsFilePath, out _);
            var group   = await service.CreateGroup("ToDelete");
            var tag     = await service.CreateTag("T1", "ToDelete", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

            // Capture state before deletion attempt
            int tagsBefore   = service.Tags.Count;
            int groupsBefore = service.GetAllGroupsFlat().Count();

            // Sabotage the save path so SaveTagsAsync will throw
            var readOnlyDir = Path.Combine(Path.GetTempPath(), "RO_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(readOnlyDir);
            _tempDirs.Add(readOnlyDir);
            var readOnlyFile = Path.Combine(readOnlyDir, "tags.json");
            File.Copy(tagsFilePath, readOnlyFile);
            Directory.CreateDirectory(readOnlyFile + ".tmp");  // forces IOException
            TagsFilePathField.SetValue(service, readOnlyFile);

            var result = await service.DeleteGroupAsync(group.Id, GroupDeletionMode.CascadeDelete);

            Assert.False(result.Success);
            Assert.Contains("rolled back", result.Message, StringComparison.OrdinalIgnoreCase);

            // Collections should be unchanged
            Assert.Equal(tagsBefore,   service.Tags.Count);
            Assert.Equal(groupsBefore, service.GetAllGroupsFlat().Count());
            Assert.NotNull(service.Tags.FirstOrDefault(t => t.Id == tag.Id));
        }

        #endregion

        #region Cancellation leaves collections unchanged

        [Fact]
        public async Task DeleteGroup_Cancellation_LeavesCollectionsUnchanged()
        {
            var service = CreateService(out _, out _);
            var group   = await service.CreateGroup("CancelGroup");
            await service.CreateTag("CT1", "CancelGroup", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

            int tagsBefore   = service.Tags.Count;
            int groupsBefore = service.GetAllGroupsFlat().Count();

            using var cts = new CancellationTokenSource();
            cts.Cancel();  // Pre-cancelled

            var result = await service.DeleteGroupAsync(
                group.Id, GroupDeletionMode.CascadeDelete, cts.Token);

            Assert.False(result.Success);
            Assert.Equal(tagsBefore,   service.Tags.Count);
            Assert.Equal(groupsBefore, service.GetAllGroupsFlat().Count());
        }

        #endregion
    }
}

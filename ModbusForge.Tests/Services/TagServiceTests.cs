using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModbusForge.Models;
using ModbusForge.Services;
using Moq;
using Xunit;

namespace ModbusForge.Tests.Services
{
    public class TagServiceTests
    {
        private static readonly FieldInfo TagsFilePathField = typeof(TagService).GetField(
            "_tagsFilePath",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static TagService CreateService(out string tagsFilePath, out Mock<ILogger<Tag>> mockLogger)
        {
            mockLogger = new Mock<ILogger<Tag>>();
            var service = new TagService(mockLogger.Object);

            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            tagsFilePath = Path.Combine(tempDir, "tags.json");

            TagsFilePathField.SetValue(service, tagsFilePath);
            return service;
        }

        private static void Cleanup(string tagsFilePath)
        {
            try
            {
                var dir = Path.GetDirectoryName(tagsFilePath);
                if (dir != null && Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        [Fact]
        public async Task CreateTag_AddsTagAndPersists()
        {
            var service = CreateService(out var tagsFilePath, out _);
            try
            {
                var tag = await service.CreateTag("TestTag", "Default", PlcArea.HoldingRegister, 1, TagDataType.UInt16);

                Assert.NotNull(tag);
                Assert.Equal("TestTag", tag.Name);
                Assert.Contains(service.Tags, t => t.Id == tag.Id);

                Assert.True(File.Exists(tagsFilePath));
                var json = await File.ReadAllTextAsync(tagsFilePath);
                using var doc = JsonDocument.Parse(json);
                var tagNames = doc.RootElement
                    .GetProperty("tags")
                    .EnumerateArray()
                    .Select(t => t.GetProperty("name").GetString());

                Assert.Contains("TestTag", tagNames);
            }
            finally
            {
                Cleanup(tagsFilePath);
            }
        }

        [Fact]
        public async Task DeleteTag_RemovesTagAndPersists()
        {
            var service = CreateService(out var tagsFilePath, out _);
            try
            {
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
            finally
            {
                Cleanup(tagsFilePath);
            }
        }

        [Fact]
        public async Task CreateGroup_AddsGroupAndPersists()
        {
            var service = CreateService(out var tagsFilePath, out _);
            try
            {
                var group = await service.CreateGroup("NewGroup");

                Assert.NotNull(group);
                Assert.Equal("NewGroup", group.Name);
                Assert.Contains(service.Groups, g => g.Name == "NewGroup");

                var json = await File.ReadAllTextAsync(tagsFilePath);
                using var doc = JsonDocument.Parse(json);
                var groupNames = doc.RootElement
                    .GetProperty("groups")
                    .EnumerateArray()
                    .Select(g => g.GetProperty("name").GetString());

                Assert.Contains("NewGroup", groupNames);
            }
            finally
            {
                Cleanup(tagsFilePath);
            }
        }
    }
}

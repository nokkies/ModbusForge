using System;
using System.IO;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.Services;

namespace ModbusForge.Tests.Services
{
    public class RegisterTemplateStoreTests : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), $"mf-templates-{Guid.NewGuid():N}");

        [Fact]
        public void Save_ThenLoad_RoundTripsTheTemplate()
        {
            var store = new RegisterTemplateStore(null, _directory);
            var template = new RegisterTemplate
            {
                Name = "ACME VFD",
                Addressing = AddressingConvention.Modicon,
                Entries =
                {
                    new RegisterTemplateEntry
                    {
                        TagName = "Speed",
                        RegisterType = PlcArea.HoldingRegister,
                        Address = 10,
                        DataType = TagDataType.Float,
                        WordOrder = WordOrder.LittleEndian,
                        Length = 2,
                        Scale = 0.1,
                        Unit = "Hz",
                        Access = RegisterAccess.ReadOnly,
                        Enum = { [0] = "Off", [1] = "On" },
                    }
                }
            };

            var path = store.Save(template);

            Assert.True(File.Exists(path));
            Assert.Equal(_directory, Path.GetDirectoryName(path));
            Assert.Equal(new[] { path }, store.ListTemplateFiles());

            var loaded = store.Load(path);
            Assert.Equal("ACME VFD", loaded.Name);
            Assert.Equal(AddressingConvention.Modicon, loaded.Addressing);

            var entry = Assert.Single(loaded.Entries);
            Assert.Equal("Speed", entry.TagName);
            Assert.Equal(TagDataType.Float, entry.DataType);
            Assert.Equal(WordOrder.LittleEndian, entry.WordOrder);
            Assert.Equal(RegisterAccess.ReadOnly, entry.Access);
            Assert.Equal("On", entry.Enum[1]);
        }

        [Fact]
        public void Save_SanitizesTheTemplateName()
        {
            var store = new RegisterTemplateStore(null, _directory);

            var path = store.Save(new RegisterTemplate { Name = "bad/name:1" });

            Assert.Equal("bad_name_1.json", Path.GetFileName(path));
        }

        [Fact]
        public void DefaultDirectory_IsUnderApplicationData()
        {
            var store = new RegisterTemplateStore();

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ModbusForge",
                "templates");
            Assert.Equal(expected, store.TemplatesDirectory);
        }

        [Fact]
        public void ListTemplateFiles_IsEmptyWhenTheDirectoryDoesNotExist()
        {
            var store = new RegisterTemplateStore(null, Path.Combine(_directory, "missing"));

            Assert.Empty(store.ListTemplateFiles());
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);

            GC.SuppressFinalize(this);
        }
    }
}

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
            Assert.NotNull(loaded);
            Assert.Equal("ACME VFD", loaded!.Name);
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

        [Theory]
        [InlineData("con")]
        [InlineData("CON")]
        [InlineData("nul")]
        [InlineData("com1")]
        [InlineData("lpt3")]
        public void Save_WindowsReservedNames_AreNotUsableAsFileNames(string name)
        {
            // Regression: a template called "con" sanitized to "CON.json", which
            // Windows rejects at runtime - the save threw after the directory
            // was created.
            var store = new RegisterTemplateStore(null, _directory);
            var template = new RegisterTemplate { Name = name };

            var path = store.Save(template); // must not throw

            Assert.True(File.Exists(path));
            // Windows matches reserved device names exactly (case-insensitive),
            // so the stored stem must no longer be the reserved name itself.
            var fileName = Path.GetFileNameWithoutExtension(path);
            Assert.False(string.Equals(fileName, name, System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Save_VeryLongNames_AreTruncatedDeterministically()
        {
            var store = new RegisterTemplateStore(null, _directory);
            var longName = new string('x', 500);
            var template = new RegisterTemplate { Name = longName };

            var path = store.Save(template);

            var fileName = Path.GetFileNameWithoutExtension(path);
            Assert.True(fileName.Length <= 240, $"file name is {fileName.Length} chars");
            Assert.StartsWith(longName[..100], fileName, StringComparison.Ordinal); // prefix, plus hash suffix

            // Saving the same template again must land on the same file.
            var path2 = store.Save(template);
            Assert.Equal(path, path2);

            // A different long name with the same prefix must not collide.
            var other = new RegisterTemplate { Name = new string('x', 499) + "y" };
            var otherPath = store.Save(other);
            Assert.NotEqual(path, otherPath);
        }

        [Fact]
        public void Save_WritesAtomically_AndLeavesNoTempFile()
        {
            var store = new RegisterTemplateStore(null, _directory);

            var path = store.Save(new RegisterTemplate { Name = "atomic" });

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Load_MissingFile_ReturnsNull()
        {
            var store = new RegisterTemplateStore(null, _directory);

            Assert.Null(store.Load(Path.Combine(_directory, "nope.json")));
        }

        [Fact]
        public void Load_InvalidJson_ReturnsNull()
        {
            var store = new RegisterTemplateStore(null, _directory);
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "broken.json");
            File.WriteAllText(path, "{ this is not json");

            Assert.Null(store.Load(path));
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

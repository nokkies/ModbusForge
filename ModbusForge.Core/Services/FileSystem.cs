using System.IO;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Default file system implementation that wraps System.IO.File.
    /// </summary>
    public class FileSystem : IFileSystem
    {
        public Task<string> ReadAllTextAsync(string path)
            => File.ReadAllTextAsync(path);

        public Task WriteAllTextAsync(string path, string contents)
            => File.WriteAllTextAsync(path, contents);

        public bool FileExists(string path)
            => File.Exists(path);
    }
}

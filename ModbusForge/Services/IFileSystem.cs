using System.IO;
using System.Threading.Tasks;

namespace ModbusForge.Services
{
    /// <summary>
    /// Abstracts file system operations to keep coordinators testable.
    /// </summary>
    public interface IFileSystem
    {
        Task<string> ReadAllTextAsync(string path);
        Task WriteAllTextAsync(string path, string contents);
        bool FileExists(string path);
    }
}

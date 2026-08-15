using System;
using System.IO;

namespace ModbusForge.Services
{
    /// <summary>
    /// Writes text files atomically: the content goes to a temporary file in the same
    /// directory first, then a rename replaces the target. A crash mid-write leaves the
    /// previous file intact instead of a truncated/corrupted one (which would lose all
    /// connection profiles / settings on the next start).
    /// </summary>
    internal static class AtomicFileWriter
    {
        public static void WriteAllText(string path, string contents)
        {
            ArgumentNullException.ThrowIfNull(path);
            contents ??= string.Empty;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Same directory (same volume) so the rename is a single atomic operation.
            var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
                throw;
            }
        }
    }
}

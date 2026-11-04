using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Helpers
{
    /// <summary>
    /// Writes a file so that a reader sees either the previous content or the complete new content, never a deleted or
    /// half-written file (TASK-721; CR-M183 for the XML store). The content goes to a temp file beside the target, which
    /// is then moved over it. A failed write deletes the temp file and leaves the target as it was.
    /// </summary>
    /// <remarks>
    /// The temp file is named <c>.{file name}.{random}.tmp</c>: it starts with a dot and ends in <c>.tmp</c>, so no
    /// store's search pattern (<c>{Name}-*</c>, <c>*.json</c>, <c>{Name}-*.xml</c>) picks one up. A process killed in
    /// the middle of a write can leave one behind; nothing reads it.
    /// </remarks>
    public static class AtomicFile
    {
        /// <summary>Writes <paramref name="path"/> through <paramref name="write"/>, atomically.</summary>
        public static void Write(string path, Action<Stream> write)
        {
            ArgumentNullException.ThrowIfNull(write);
            var temp = TempPathFor(path);
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                }
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        /// <summary>Writes <paramref name="path"/> through <paramref name="write"/>, atomically.</summary>
        public static async Task WriteAsync(string path, Func<Stream, Task> write, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            var temp = TempPathFor(path);
            try
            {
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await write(stream).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        /// <summary>
        /// Deletes <paramref name="directory"/> when it exists and holds nothing, and leaves it otherwise: a store's
        /// Destroy removes its own files, never what someone else keeps beside them (TASK-721).
        /// </summary>
        /// <returns>Whether the directory was deleted.</returns>
        public static bool DeleteDirectoryIfEmpty(string? directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return false;
            }
            Directory.Delete(directory);
            return true;
        }

        private static string TempPathFor(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? string.Empty;
            return System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        }

        private static void TryDelete(string temp)
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException)
            {
                // best effort: the target is intact either way
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

using System;
using System.IO;

namespace Birko.Data.Stores
{
    /// <summary>
    /// The folder and file names a file-backed <see cref="IStoreFactory"/> (JSON, XML) uses (TASK-990), in one place so the
    /// two backends read their connection string, and name their files, the same way.
    /// </summary>
    public static class FileStoreFolders
    {
        /// <summary>
        /// The absolute folder <paramref name="connectionString"/> names: a bare path, or <c>Path=…</c> with no other key.
        /// Refused by name when empty.
        /// </summary>
        public static string Parse(string backend, string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException($"Store backend '{backend}' needs a folder path (or Path=…).", nameof(connectionString));
            var path = connectionString.Contains('=')
                ? StoreFactory.ParseKeys(backend, connectionString, "Path").TryGetValue("Path", out var p) ? p : string.Empty
                : connectionString.Trim();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"Store backend '{backend}' needs a folder path in Path=….", nameof(connectionString));
            return Path.GetFullPath(path);
        }

        /// <summary>The file of <typeparamref name="T"/>'s store: the type's full name (nested types joined by a dot) plus <paramref name="extension"/>.</summary>
        public static string FileName<T>(string extension) => (typeof(T).FullName ?? typeof(T).Name).Replace('+', '.') + extension;
    }
}

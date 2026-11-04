using Birko.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Async JSON bulk store that stores each item in a separate file with bulk operations support.
    /// Files are named using the pattern: {Name}-{Guid}.json
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncJsonSeparateBulkStore<T> : AsyncJsonStore<T>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// Dictionary tracking which file each item is stored in.
        /// </summary>
        private Dictionary<Guid, string> _files = null!;

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AsyncJsonSeparateBulkStore class.
        /// </summary>
        public AsyncJsonSeparateBulkStore() : base()
        {
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        protected override Task InitCoreAsync(CancellationToken ct = default)
        {
            var pathDirectory = PathDirectory;
            if (!string.IsNullOrEmpty(pathDirectory) && !Directory.Exists(pathDirectory))
            {
                Directory.CreateDirectory(pathDirectory);
            }
            _files = new Dictionary<Guid, string>();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task DestroyCoreAsync(CancellationToken ct = default)
        {
            _items?.Clear();
            _files.Clear();
            _loaded = false;

            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory) || !Directory.Exists(pathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                return;
            }

            var files = Birko.Helpers.RecordFiles.List(pathDirectory, JsonFileNaming.SearchPattern(_settings.Name), Birko.Helpers.RecordFiles.IsGuid).ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    await Task.Run(() => File.Delete(file), ct);
                }
            }

            Birko.Helpers.AtomicFile.DeleteDirectoryIfEmpty(pathDirectory);   // TASK-721: never what else is there
        }

        #endregion

        #region File Management

        /// <summary>
        /// Adds a file mapping for an item.
        /// </summary>
        /// <param name="guid">The item GUID.</param>
        /// <param name="name">The file name.</param>
        protected void AddFile(Guid guid, string name)
        {
            _files ??= new Dictionary<Guid, string>();
            _files[guid] = name;
        }

        #endregion

        #region Data Persistence

        /// <inheritdoc />
        protected override async Task LoadDataAsync(CancellationToken ct)
        {
            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory) || !Directory.Exists(pathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                _items ??= new();
                return;
            }

            var files = Birko.Helpers.RecordFiles.List(pathDirectory, JsonFileNaming.SearchPattern(_settings.Name), Birko.Helpers.RecordFiles.IsGuid);
            if (files.Length == 0)
            {
                return;
            }

            foreach (var file in files)
            {
                using var fileStream = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                var item = await ReadFromStreamAsync<T>(fileStream, ct);
                if (item?.Guid.HasValue ?? false)
                {
                    _items.Add(item.Guid.Value, item);
                    AddFile(item.Guid.Value, file);
                }
            }
        }

        /// <inheritdoc />
        protected override async Task SaveDataAsync(CancellationToken ct)
        {
            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                return;
            }

            if (!Directory.Exists(pathDirectory))
            {
                Directory.CreateDirectory(pathDirectory);
            }

            var removedFiles = Birko.Helpers.RecordFiles.List(pathDirectory, JsonFileNaming.SearchPattern(_settings.Name), Birko.Helpers.RecordFiles.IsGuid).ToDictionary(x => x);

            foreach (var item in _items)
            {
                if (!_files.ContainsKey(item.Key))
                {
                    var fileName = _settings.Name.Contains('*')
                        ? _settings.Name.Replace("*", item.Key.ToString("D"))
                        : $"{_settings.Name}-{item.Key:D}";

                    // Validate the combined path even though fileName is constructed internally
                    var path = PathValidator.CombineAndValidate(
                        pathDirectory ?? throw new InvalidOperationException("Path cannot be null"),
                        fileName);

                    _files.Add(item.Key, path);
                }

                var target = _files[item.Key];
                await Birko.Helpers.AtomicFile.WriteAsync(target, stream => WriteToStreamAsync(stream, item.Value, ct), ct);   // TASK-721

                if (removedFiles.ContainsKey(_files[item.Key]))
                {
                    removedFiles.Remove(_files[item.Key]);
                }
            }

            foreach (var kvp in removedFiles)
            {
                await Task.Run(() => File.Delete(kvp.Value), ct);
            }
        }

        #endregion
    }
}

using Birko.Data.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
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
        private Dictionary<Guid, string> _files = null;

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
        public override Task InitAsync(CancellationToken ct = default)
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
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            _items?.Clear();
            _files.Clear();

            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory) || !Directory.Exists(pathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                return;
            }

            var files = Directory.GetFiles(pathDirectory, _settings.Name).ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    await Task.Run(() => File.Delete(file), ct);
                }
            }

            await Task.Run(() => Directory.Delete(pathDirectory), ct);
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

            var files = Directory.GetFiles(pathDirectory, _settings.Name);
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

            var removedFiles = Directory.GetFiles(pathDirectory, _settings.Name).ToDictionary(x => x);

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

                await Task.Run(() => File.Delete(_files[item.Key]), ct);

                using var fileStream = new FileStream(
                    _files[item.Key],
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true);

                await WriteToStreamAsync(fileStream, item.Value, ct);

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

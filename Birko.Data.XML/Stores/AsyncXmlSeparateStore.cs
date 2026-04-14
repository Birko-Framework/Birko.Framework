using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// Async XML file-based data store that stores each entity in a separate file.
    /// Files are named using the pattern: {Name}-{Guid}.xml
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncXmlSeparateStore<T>
        : AsyncXmlStore<T>
        , ISettingsStore<Settings>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// Mapping of entity GUIDs to their file paths.
        /// </summary>
        private Dictionary<Guid, string> _files = null!;

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AsyncXmlSeparateStore class.
        /// </summary>
        public AsyncXmlSeparateStore() : base()
        {
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        protected override async Task InitCoreAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(PathDirectory) && !Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory!);
            }
            _files = new Dictionary<Guid, string>();
            await Task.CompletedTask;
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            _items?.Clear();
            _files.Clear();
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                return;
            }
            var files = Directory.GetFiles(PathDirectory!, _settings.Name + "-*.xml").ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    await Task.Run(() => File.Delete(file), ct);
                }
            }
            await Task.Run(() => Directory.Delete(PathDirectory!), ct);
        }

        #endregion

        #region File Management

        /// <summary>
        /// Adds a file mapping for an entity.
        /// </summary>
        /// <param name="guid">The entity GUID.</param>
        /// <param name="name">The file path.</param>
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
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                _items ??= new();
                return;
            }

            var files = await Task.Run(() => Directory.GetFiles(PathDirectory!, _settings.Name + "-*.xml").ToArray(), ct);
            if (!files.Any())
            {
                _items = new();
                return;
            }

            _items = new();
            foreach (var file in files)
            {
                try
                {
                    using FileStream fileStream = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 4096,
                        useAsync: true);

                    var item = await ReadFromStreamAsync<T>(fileStream, ct);
                    if (item != null && item.Guid.HasValue)
                    {
                        _items.Add(item.Guid.Value, item);
                        AddFile(item.Guid.Value, file);
                    }
                }
                catch
                {
                    // Skip files that cannot be read
                    continue;
                }
            }
        }

        /// <inheritdoc />
        protected override async Task SaveDataAsync(CancellationToken ct)
        {
            // Each entity is saved individually in its own file
            // This is handled in CreateCoreAsync, UpdateCoreAsync, and DeleteCoreAsync
            await Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items.Add(data.Guid.Value, data);

            var fileName = $"{_settings.Name}-{data.Guid.Value}.xml";
            var filePath = PathValidator.CombineAndValidate(PathDirectory!, fileName);

            AddFile(data.Guid.Value, filePath);

            await Task.Run(() =>
            {
                using FileStream fileStream = File.OpenWrite(filePath);
                WriteToStream(fileStream, data);
            }, ct);

            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;

                if (_files.ContainsKey(data.Guid.Value))
                {
                    var filePath = _files[data.Guid.Value];
                    await Task.Run(() =>
                    {
                        File.Delete(filePath);
                        using FileStream fileStream = File.OpenWrite(filePath);
                        WriteToStream(fileStream, data);
                    }, ct);
                }
            }
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                _items.Remove(data.Guid.Value);

                if (_files.ContainsKey(data.Guid.Value))
                {
                    var filePath = _files[data.Guid.Value];
                    await Task.Run(() => File.Delete(filePath), ct);
                    _files.Remove(data.Guid.Value);
                }
            }
        }

        #endregion
    }
}

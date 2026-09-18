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
    /// Async XML file-based data store that stores entities in batched files.
    /// Entities are grouped into batch files based on the configured batch size.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncXmlBatchStore<T>
        : AsyncXmlSeparateStore<T>
        , ISettingsStore<Settings>
        , ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The maximum number of entities per batch file.
        /// </summary>
        private int _batchSize = 1024;

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AsyncXmlBatchStore class.
        /// </summary>
        public AsyncXmlBatchStore() : base()
        {
        }

        /// <summary>
        /// Sets the batch settings for the store.
        /// </summary>
        /// <param name="settings">The batch settings to apply.</param>
        /// <exception cref="InvalidDataException">Thrown when settings is not a BatchSettings instance.</exception>
        public override void SetSettings(Settings settings)
        {
            if (settings is not BatchSettings batchSettings)
            {
                throw new InvalidDataException(nameof(settings));
            }
            _batchSize = batchSettings.BatchSize;
            base.SetSettings(settings);
        }

        // NOTE: intentionally NOT redeclaring SetSettings(ISettings) with `new` — see XmlBatchStore
        // for why (it hid the SetSettings(Settings) override from member lookup and recursed forever).
        // The inherited AsyncXmlStore.SetSettings(ISettings) satisfies ISettingsStore<ISettings>.

        #endregion

        #region Data Persistence

        /// <summary>
        /// Persists all items as batched List&lt;T&gt; files named {Name}-{index}.xml, matching
        /// LoadDataAsync's ReadFromStreamAsync&lt;List&lt;T&gt;&gt;. The inherited AsyncXmlSeparateStore
        /// wrote a single entity per file, so batch round-trips lost all data (CR-C21).
        /// </summary>
        protected override async Task SaveDataAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(PathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                return;
            }
            if (!Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory!);
            }

            foreach (var file in Directory.GetFiles(PathDirectory!, _settings.Name + "-*.xml"))
            {
                File.Delete(file);
            }

            var all = _items.Values.ToList();
            var size = _batchSize > 0 ? _batchSize : 1024;
            for (int offset = 0, batchIndex = 0; offset < all.Count; offset += size, batchIndex++)
            {
                var chunk = all.Skip(offset).Take(size).ToList();
                var fileName = $"{_settings.Name}-{batchIndex}.xml";
                var filePath = PathValidator.CombineAndValidate(PathDirectory!, fileName);
                using FileStream fileStream = new FileStream(
                    filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
                await WriteToStreamAsync(fileStream, chunk, ct);
            }
        }

        // Batch files hold List<T>, so the per-entity single-item writes inherited from
        // AsyncXmlSeparateStore don't apply. Mutate _items and rewrite the batch files via
        // SaveDataAsync for every CRUD path (single + bulk).

        /// <inheritdoc />
        protected override async Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items[data.Guid.Value] = data;
            await SaveDataAsync(ct);
            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(T data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            if (data.Guid != null && _items.ContainsKey(data.Guid.Value))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
                await SaveDataAsync(ct);
            }
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(T data, CancellationToken ct = default)
        {
            if (data.Guid != null && _items.Remove(data.Guid.Value))
            {
                await SaveDataAsync(ct);
            }
        }

        /// <inheritdoc />
        protected override async Task CreateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            var changed = false;
            foreach (var item in data.Where(x => x != null))
            {
                item.Guid ??= Guid.NewGuid();
                storeDelegate?.Invoke(item);
                _items[item.Guid.Value] = item;
                changed = true;
            }
            if (changed) await SaveDataAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task UpdateCoreAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            var changed = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && _items.ContainsKey(item.Guid.Value))
                {
                    storeDelegate?.Invoke(item);
                    _items[item.Guid.Value] = item;
                    changed = true;
                }
            }
            if (changed) await SaveDataAsync(ct);
        }

        /// <inheritdoc />
        protected override async Task DeleteCoreAsync(IEnumerable<T> data, CancellationToken ct = default)
        {
            var changed = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && _items.Remove(item.Guid.Value))
                {
                    changed = true;
                }
            }
            if (changed) await SaveDataAsync(ct);
        }

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

                    var items = await ReadFromStreamAsync<List<T>>(fileStream, ct);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (item.Guid.HasValue)
                            {
                                _items.Add(item.Guid.Value, item);
                            }
                        }
                    }
                }
                catch
                {
                    // Skip files that cannot be read
                    continue;
                }
            }
        }

        #endregion
    }
}

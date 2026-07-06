using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// XML file-based data store that stores entities in batched files.
    /// Entities are grouped into batch files based on the configured batch size.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class XmlBatchStore<T>
        : XmlSeparateStore<T>
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
        /// Initializes a new instance of the XmlBatchStore class.
        /// </summary>
        public XmlBatchStore() : base()
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

        // NOTE: intentionally NOT redeclaring SetSettings(ISettings) with `new`. Doing so made it the
        // only SetSettings visible via member lookup (the SetSettings(Settings) override does not count
        // as a declaration), so SetSettings(ISettings) called itself forever — a stack overflow that
        // made batch stores unconfigurable. The inherited XmlStore.SetSettings(ISettings) already
        // satisfies ISettingsStore<ISettings> and dispatches to the SetSettings(Settings) override.

        #endregion

        #region Data Persistence

        /// <summary>
        /// Persists all items as batched List&lt;T&gt; files named {Name}-{index}.xml. This matches
        /// LoadData's ReadFromStream&lt;List&lt;T&gt;&gt;; the inherited XmlSeparateStore wrote a single
        /// entity per file, so batch round-trips lost all data (CR-C21). Existing batch files are
        /// rewritten from scratch on every save.
        /// </summary>
        protected override void SaveData()
        {
            if (string.IsNullOrEmpty(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }
            if (!Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory!);
            }

            // Clear stale batch files, then rewrite the current items in chunks of _batchSize.
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
                using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                WriteToStream(fileStream, chunk);
            }
        }

        // Batch files hold List<T>, so the per-entity single-item writes inherited from
        // XmlSeparateStore don't apply. Mutate the in-memory dictionary and rewrite the batch
        // files via SaveData for every CRUD path (single + bulk).

        /// <inheritdoc />
        protected override Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items[data.Guid.Value] = data;
            SaveData();
            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data.Guid != null && _items.ContainsKey(data.Guid.Value))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
                SaveData();
            }
        }

        /// <inheritdoc />
        protected override void DeleteCore(T data)
        {
            if (data.Guid != null && _items.Remove(data.Guid.Value))
            {
                SaveData();
            }
        }

        /// <inheritdoc />
        protected override void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            var changed = false;
            foreach (var item in data.Where(x => x != null))
            {
                item.Guid ??= Guid.NewGuid();
                storeDelegate?.Invoke(item);
                _items[item.Guid.Value] = item;
                changed = true;
            }
            if (changed) SaveData();
        }

        /// <inheritdoc />
        protected override void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
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
            if (changed) SaveData();
        }

        /// <inheritdoc />
        protected override void DeleteCore(IEnumerable<T> data)
        {
            var changed = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && _items.Remove(item.Guid.Value))
                {
                    changed = true;
                }
            }
            if (changed) SaveData();
        }

        /// <inheritdoc />
        protected override void LoadData()
        {
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                _items ??= new();
                return;
            }
            var files = Directory.GetFiles(PathDirectory!, _settings.Name + "-*.xml").ToArray();
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
                    using FileStream fileStream = File.OpenRead(file);
                    var items = ReadFromStream<List<T>>(fileStream);
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

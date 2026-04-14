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
    /// XML file-based data store that stores each entity in a separate file.
    /// Files are named using the pattern: {Name}-{Guid}.xml
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class XmlSeparateStore<T>
        : XmlStore<T>
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
        /// Initializes a new instance of the XmlSeparateStore class.
        /// </summary>
        public XmlSeparateStore() : base()
        {
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        protected override void InitCore()
        {
            if (!string.IsNullOrEmpty(PathDirectory) && !Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory!);
            }
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            _items?.Clear();
            _files.Clear();
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }
            var files = Directory.GetFiles(PathDirectory!, _settings.Name + "-*.xml").ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            Directory.Delete(PathDirectory!);
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
                    var item = ReadFromStream<T>(fileStream);
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
        protected override void SaveData()
        {
            // Each entity is saved individually in its own file
            // This is handled in CreateCore, UpdateCore, and DeleteCore
        }

        /// <inheritdoc />
        protected override Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            data.Guid ??= Guid.NewGuid();
            storeDelegate?.Invoke(data);
            _items.Add(data.Guid.Value, data);

            var fileName = $"{_settings.Name}-{data.Guid.Value}.xml";
            var filePath = PathValidator.CombineAndValidate(PathDirectory!, fileName);

            AddFile(data.Guid.Value, filePath);

            using FileStream fileStream = File.OpenWrite(filePath);
            WriteToStream(fileStream, data);

            return data.Guid.Value;
        }

        /// <inheritdoc />
        protected override void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;

                if (_files.ContainsKey(data.Guid.Value))
                {
                    var filePath = _files[data.Guid.Value];
                    File.Delete(filePath);
                    using FileStream fileStream = File.OpenWrite(filePath);
                    WriteToStream(fileStream, data);
                }
            }
        }

        /// <inheritdoc />
        protected override void DeleteCore(T data)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                _items.Remove(data.Guid.Value);

                if (_files.ContainsKey(data.Guid.Value))
                {
                    var filePath = _files[data.Guid.Value];
                    File.Delete(filePath);
                    _files.Remove(data.Guid.Value);
                }
            }
        }

        #endregion
    }
}

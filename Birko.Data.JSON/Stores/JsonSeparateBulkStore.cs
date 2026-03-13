using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Helpers;

namespace Birko.Data.Stores
{
    /// <summary>
    /// JSON file-based bulk data store that stores each entity in a separate file.
    /// Files are named using the pattern: {Name}-{Guid}.json
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class JsonSeparateBulkStore<T>
        : JsonStore<T>
        , ISettingsStore<ISettings>
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
        /// Initializes a new instance of the JsonSeparateBulkStore class.
        /// </summary>
        public JsonSeparateBulkStore() : base()
        {
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        public override void Init()
        {
            if (!string.IsNullOrEmpty(PathDirectory) && !Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory);
            }
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            _items?.Clear();
            _files.Clear();
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || !string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }
            var files = Directory.GetFiles(PathDirectory, _settings.Name).ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            Directory.Delete(Path!);
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

            var files = Directory.GetFiles(PathDirectory, _settings.Name).ToArray();
            if (!files.Any())
            {
                return;
            }

            foreach (var file in files)
            {
                using FileStream fileStream = File.OpenRead(file);
                var item = ReadFromStream<T>(fileStream);
                if (item?.Guid.HasValue == true)
                {
                    _items.Add(item.Guid!.Value, item);
                    AddFile(item.Guid.Value, file);
                }
            }
        }

        /// <inheritdoc />
        protected override void SaveData()
        {
            if (string.IsNullOrEmpty(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }
            if (!Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory);
            }
            var removedFiles = Directory.GetFiles(PathDirectory, _settings.Name).ToDictionary(x => x);

            foreach (var item in _items)
            {
                if (_files.ContainsKey(item.Key))
                {
                    var fileName = _settings.Name.Contains('*') ? _settings.Name.Replace("*", item.Key.ToString("D")) : $"{_settings.Name}-{item.Key:D}";
                    // Validate the combined path even though fileName is constructed internally
                    var path = PathValidator.CombineAndValidate(PathDirectory ?? throw new InvalidOperationException("PathDirectory cannot be null"), fileName);
                    _files[item.Key] = path;
                    File.Delete(_files[item.Key]);
                    using FileStream fileStream = File.OpenWrite(_files[item.Key]);
                    WriteToStream(fileStream, item.Value);
                }

                if (removedFiles.ContainsKey(_files[item.Key]))
                {
                    removedFiles.Remove(_files[item.Key]);
                }
            }

            foreach (var kvp in removedFiles)
            {
                File.Delete(kvp.Value);
            }
        }

        #endregion
    }
}

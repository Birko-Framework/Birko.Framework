using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// JSON file-based data store that stores each entity in a separate file.
    /// Files are named using the pattern: {Name}-{Guid}.json
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class JsonSeparateStore<T>
        : JsonStore<T>
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
        /// Initializes a new instance of the JsonSeparateStore class.
        /// </summary>
        public JsonSeparateStore() : base()
        {
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        protected override void InitCore()
        {
            if (!string.IsNullOrEmpty(Path) && !Directory.Exists(Path))
            {
                Directory.CreateDirectory(Path);
            }
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            _items?.Clear();
            _files.Clear();
            if (string.IsNullOrEmpty(Path) || !Directory.Exists(Path) || !string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }
            var files = Directory.GetFiles(Path, _settings.Name).ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            Directory.Delete(Path);
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
            if (string.IsNullOrEmpty(Path) || !Directory.Exists(Path) || string.IsNullOrEmpty(_settings.Name))
            {
                _items ??= new();
                return;
            }

            var files = Directory.GetFiles(Path, _settings.Name).ToArray();
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
            if (string.IsNullOrEmpty(Path) || string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }

            if (!Directory.Exists(Path))
            {
                Directory.CreateDirectory(Path);
            }

            var removedFiles = Directory.GetFiles(Path, _settings.Name).ToDictionary(x => x);

            foreach (var item in _items)
            {
                if (_files.ContainsKey(item.Key))
                {
                    var fileName = _settings.Name.Contains('*') ? _settings.Name.Replace("*", item.Key.ToString("D")) : $"{_settings.Name}-{item.Key.ToString("D")}";
                    // Validate the combined path even though fileName is constructed internally
                    var path = PathValidator.CombineAndValidate(Path ?? throw new InvalidOperationException("Path cannot be null"), fileName);
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

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
            if (!string.IsNullOrEmpty(PathDirectory) && !Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory);
            }
            _files = new Dictionary<Guid, string>();
        }

        /// <inheritdoc />
        protected override void DestroyCore()
        {
            _items?.Clear();
            _files.Clear();
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }
            var files = Birko.Helpers.RecordFiles.List(PathDirectory, JsonFileNaming.SearchPattern(_settings.Name), Birko.Helpers.RecordFiles.IsGuid).ToArray();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            Birko.Helpers.AtomicFile.DeleteDirectoryIfEmpty(PathDirectory);   // TASK-721: never what else is there
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

            var files = Birko.Helpers.RecordFiles.List(PathDirectory, JsonFileNaming.SearchPattern(_settings.Name), Birko.Helpers.RecordFiles.IsGuid).ToArray();
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

            var removedFiles = Birko.Helpers.RecordFiles.List(PathDirectory, JsonFileNaming.SearchPattern(_settings.Name), Birko.Helpers.RecordFiles.IsGuid).ToDictionary(x => x);

            foreach (var item in _items)
            {
                // A newly-created entity is present in _items but not yet in _files, so its file path
                // must be computed and registered here — otherwise the write below is skipped and the
                // unconditional _files[item.Key] access throws KeyNotFoundException on first save
                // (see CODE-REVIEW-AUDIT CR-C08). Mirrors AsyncJsonSeparateStore.SaveDataAsync.
                if (!_files.ContainsKey(item.Key))
                {
                    var fileName = _settings.Name.Contains('*') ? _settings.Name.Replace("*", item.Key.ToString("D")) : $"{_settings.Name}-{item.Key.ToString("D")}";
                    // Validate the combined path even though fileName is constructed internally
                    var path = PathValidator.CombineAndValidate(PathDirectory ?? throw new InvalidOperationException("PathDirectory cannot be null"), fileName);
                    _files.Add(item.Key, path);
                }

                Birko.Helpers.AtomicFile.Write(_files[item.Key], stream => WriteToStream(stream, item.Value));   // TASK-721

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

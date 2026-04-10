using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// JSON file-based data store that stores all entities in a single JSON file.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class JsonStore<T>
        : AbstractJsonStore<T>
        , ISettingsStore<Settings>
        , ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The settings for this JSON store.
        /// </summary>
        protected Settings _settings = null!;

        /// <summary>
        /// Gets the full file path for the JSON data file.
        /// </summary>
        public string? Path
        {
            get
            {
                return GetPath();
            }
        }

        /// <summary>
        /// Gets the directory path where the JSON file is stored.
        /// </summary>
        public string? PathDirectory
        {
            get
            {
                return GetDirectory();
            }
        }

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the JsonStore class.
        /// </summary>
        public JsonStore() : base()
        {
        }

        /// <summary>
        /// Sets the store settings and initializes the store.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public virtual void SetSettings(Settings settings)
        {
            _settings = settings;
            Init();
            LoadData();
        }

        /// <summary>
        /// Sets the store settings using the ISettings interface.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public virtual void SetSettings(ISettings settings)
        {
            if (settings is Settings settings1)
            {
                SetSettings(settings1);
            }
        }

        /// <inheritdoc />
        protected override void InitCore()
        {
            if (!string.IsNullOrEmpty(Path) && !File.Exists(Path) && (_settings is Settings settings))
            {
                try
                {
                    var directory = GetDirectory();
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(Path, "[]");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize JSON store. Location: '{settings.Location}', Name: '{settings.Name}'. " +
                        $"See inner exception for details.",
                        ex);
                }
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            _items?.Clear();
            if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            {
                File.Delete(Path);
            }
        }

        #endregion

        #region Path Configuration

        /// <summary>
        /// Gets the full file path for the JSON data file.
        /// </summary>
        /// <returns>The validated file path.</returns>
        public virtual string? GetPath()
        {
            if (string.IsNullOrEmpty(_settings?.Location) || string.IsNullOrEmpty(_settings?.Name))
            {
                return null;
            }

            var directory = GetDirectory();
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            try
            {
                // Validate the path to prevent directory traversal attacks
                return PathValidator.CombineAndValidate(directory, _settings.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid path configuration for store. Location: '{_settings.Location}', Name: '{_settings.Name}'. " +
                    $"See inner exception for details.",
                    ex);
            }
        }

        /// <summary>
        /// Gets the directory path where the JSON file is stored.
        /// </summary>
        /// <returns>The validated directory path.</returns>
        public virtual string? GetDirectory()
        {
            if (string.IsNullOrEmpty(_settings?.Location))
            {
                return null;
            }

            try
            {
                // Validate the directory path
                return PathValidator.ValidateDirectory(_settings.Location);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid directory configuration for store. Location: '{_settings.Location}'. " +
                    $"See inner exception for details.",
                    ex);
            }
        }

        #endregion

        #region Data Persistence

        /// <inheritdoc />
        protected override void LoadData()
        {
            if (string.IsNullOrEmpty(Path) || !File.Exists(Path))
            {
                _items ??= new();
                return;
            }
            using FileStream fileStream = File.OpenRead(Path);
            var items = ReadFromStream<List<T>>(fileStream);
            _items = new();
            if (items != null)
            {
                foreach (var item in items)
                {
                    _items.Add(item.Guid!.Value, item);
                }
            }
        }

        /// <inheritdoc />
        protected override void SaveData()
        {
            if (string.IsNullOrEmpty(Path))
            {
                return;
            }
            File.Delete(Path);
            using FileStream fileStream = File.OpenWrite(Path);
            WriteToStream(fileStream, _items);
        }

        #endregion
    }
}

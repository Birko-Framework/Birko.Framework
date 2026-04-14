using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Serialization;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// XML file-based data store that stores all entities in a single XML file.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class XmlStore<T>
        : AbstractXmlStore<T>
        , ISettingsStore<Settings>
        , ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The settings for this XML store.
        /// </summary>
        protected Settings _settings = null!;

        /// <summary>
        /// Gets the full file path for the XML data file.
        /// </summary>
        public string? Path
        {
            get
            {
                return GetPath();
            }
        }

        /// <summary>
        /// Gets the directory path where the XML file is stored.
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
        /// Initializes a new instance of the XmlStore class.
        /// </summary>
        public XmlStore() : base()
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

                    // Create empty XML file with root element
                    var rootName = typeof(T).Name + "s";
                    File.WriteAllText(Path, $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ArrayOf{typeof(T).Name} xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" />\n");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize XML store. Location: '{settings.Location}', Name: '{settings.Name}'. " +
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
        /// Gets the full file path for the XML data file.
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
                // Ensure the filename has .xml extension
                var fileName = _settings.Name;
                if (!fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".xml";
                }

                // Validate the path to prevent directory traversal attacks
                return PathValidator.CombineAndValidate(directory, fileName);
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
        /// Gets the directory path where the XML file is stored.
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
                    if (item.Guid.HasValue)
                    {
                        _items.Add(item.Guid.Value, item);
                    }
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
            WriteToStream(fileStream, _items.Values.ToList());
        }

        #endregion
    }
}

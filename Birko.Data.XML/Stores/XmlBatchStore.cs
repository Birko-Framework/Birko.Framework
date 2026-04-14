using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// Sets the store settings using the ISettings interface.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public new virtual void SetSettings(ISettings settings)
        {
            if (settings is Settings settings1)
            {
                SetSettings(settings1);
            }
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

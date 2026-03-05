using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Data.Helpers;

namespace Birko.Data.Stores
{
    /// <summary>
    /// JSON file-based data store that stores entities in batched files.
    /// Entities are grouped into batch files based on the configured batch size.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class JsonBatchStore<T>
        : JsonSeparateStore<T>
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
        /// Initializes a new instance of the JsonBatchStore class.
        /// </summary>
        public JsonBatchStore() : base()
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
            if (string.IsNullOrEmpty(Path) || !Directory.Exists(Path) || string.IsNullOrEmpty(_settings.Name))
            {
                _items ??= new();
                return;
            }
            var files = Directory.GetFiles(Path, _settings.Name).ToArray();
            if (files.Any())
            {
                _items = new();
                int batch = 1;
                foreach (var file in files)
                {
                    using FileStream fileStream = File.OpenRead(file);
                    var items = ReadFromStream<IEnumerable<T>>(fileStream);
                    foreach (var item in items)
                    {
                        _items.Add(item.Guid.Value, item);
                    }
                    byte[] bytes = new byte[16];
                    BitConverter.GetBytes(batch).CopyTo(bytes, 0);
                    AddFile(new Guid(bytes), file);
                    batch++;
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

            var removedFiles = Directory.GetFiles(Path, _settings.Name).ToDictionary(x => x);

            int batch = 1;
            List<T> batchFiles = new();
            foreach (var item in _items)
            {
                batchFiles.Add(item.Value);
                if (batchFiles.Count == _batchSize)
                {
                    SaveBatch(batch, batchFiles, removedFiles);
                    batch++;
                    batchFiles.Clear();
                }
            }

            if (batchFiles.Any())
            {
                SaveBatch(batch, batchFiles, removedFiles);
            }

            if (removedFiles.Any())
            {
                foreach (var kvp in removedFiles)
                {
                    File.Delete(kvp.Value);
                }
            }
        }

        /// <summary>
        /// Saves a batch of entities to a file.
        /// </summary>
        /// <param name="batch">The batch number.</param>
        /// <param name="batchFiles">The entities in the batch.</param>
        /// <param name="removedFiles">Dictionary of files to remove after saving.</param>
        private void SaveBatch(int batch, List<T> batchFiles, Dictionary<string, string> removedFiles)
        {
            if (!Directory.Exists(Path))
            {
                Directory.CreateDirectory(Path);
            }

            byte[] bytes = new byte[16];
            BitConverter.GetBytes(batch).CopyTo(bytes, 0);
            var guid = new Guid(bytes);
            var fileName = _settings.Name.Contains('*') ? _settings.Name.Replace("*", guid.ToString("D")) : $"{_settings.Name}-{guid.ToString("D")}";
            // Validate the combined path even though fileName is constructed internally
            var path = PathValidator.CombineAndValidate(Path ?? throw new InvalidOperationException("Path cannot be null"), fileName);
            AddFile(new Guid(bytes), path);
            File.Delete(path);
            using FileStream fileStream = File.OpenWrite(path);
            WriteToStream(fileStream, batchFiles);

            if (removedFiles.ContainsKey(path))
            {
                removedFiles.Remove(path);
            }
        }

        #endregion
    }
}

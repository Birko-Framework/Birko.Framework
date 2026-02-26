using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Birko.Data.Helpers;

namespace Birko.Data.Stores
{
    public class JsonBatchBulkStore<T>
        : JsonSeparateBulkStore<T>
        , ISettingsStore<Settings>
        , ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        private int _batchSize = 1024;
        public JsonBatchBulkStore() : base()
        {
        }

        public override void SetSettings(Settings settings)
        {
            if (settings is not BatchSettings)
            {
                throw new InvalidDataException(nameof(settings));
            }
            _batchSize = ((BatchSettings)settings).BatchSize;
            base.SetSettings(settings);
        }

        protected override void LoadData()
        {
            if (string.IsNullOrEmpty(PathDirectory) || !Directory.Exists(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                _items ??= new();
                return;
            }
            var files = Directory.GetFiles(PathDirectory, _settings.Name).ToArray();
            if (files.Any())
            {
                _items = new();
                int batch = 1;
                foreach (var file in files)
                {
                    using FileStream fileStrem = File.OpenRead(file);
                    var items = ReadFromStream<IEnumerable<T>>(fileStrem);
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

        protected override void SaveData()
        {
            if (string.IsNullOrEmpty(PathDirectory) || string.IsNullOrEmpty(_settings.Name))
            {
                return;
            }

            var removedFiles = Directory.GetFiles(PathDirectory, _settings.Name).ToDictionary(x => x);

            int batch = 1;
            List<T> batchFiles = new();
            foreach (var item in _items)
            {
                batchFiles.Add(item.Value);
                if (batchFiles.Count == _batchSize)
                {
                    SaveBatch(batch, batchFiles.ToArray(), removedFiles);
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

        private void SaveBatch(int batch, IEnumerable<T> batchFiles, Dictionary<string, string> removedFiles)
        {
            if (!Directory.Exists(PathDirectory))
            {
                Directory.CreateDirectory(PathDirectory);
            }

            byte[] bytes = new byte[16];
            BitConverter.GetBytes(batch).CopyTo(bytes, 0);
            var guid = new Guid(bytes);
            var fileName = _settings.Name.Contains('*') ? _settings.Name.Replace("*", guid.ToString("D")) : $"{_settings.Name}-{guid.ToString("D")}";
            // Validate the combined path even though fileName is constructed internally
            var path = PathValidator.CombineAndValidate(PathDirectory ?? throw new InvalidOperationException("PathDirectory cannot be null"), fileName);
            AddFile(new Guid(bytes), path);
            File.Delete(path);
            using FileStream fileStream = File.OpenWrite(path);
            WriteToStream(fileStream, batchFiles);

            if (removedFiles.ContainsKey(path))
            {
                removedFiles.Remove(path);
            }
            fileStream.Close();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonBatchBulkStore<T>
        : JsonSeparateBulkStore<T>
        , ISettingsStore<Settings>
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
            var path = System.IO.Path.Combine(Path, fileName);
            AddFile(new Guid(bytes), path);

            using FileStream fileStream = File.OpenWrite(path);
            WriteToStream(fileStream, batchFiles);

            if (removedFiles.ContainsKey(path))
            {
                removedFiles.Remove(path);
            }
        }
    }
}

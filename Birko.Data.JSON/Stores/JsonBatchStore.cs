using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonBatchStore<T> : JsonSeparateStore<T>
        where T: Models.AbstractModel
    {
        private int _batchSize = 1024;
        public JsonBatchStore(): base()
        {
        }

        public override void SetSettings(ISettings settings)
        {
            if (settings is BatchSettings batchSettings)
            {
                _batchSize = batchSettings.BatchSize;
            }
            base.SetSettings(settings);
        }

        public override string GetPath()
        {
            return ((_settings is Settings settings) && !string.IsNullOrEmpty(settings.Location))
                    ? settings.Location
                    : null;
        }

        public override void Load()
        {
            var settings = (_settings as Settings);
            if (!string.IsNullOrEmpty(Path) && System.IO.Directory.Exists(Path) && !string.IsNullOrEmpty(settings.Name))
            {
                var files = System.IO.Directory.GetFiles(Path, settings.Name).ToArray();
                if (files.Any())
                {
                    _items = new List<T>();
                    int batch = 1;
                    foreach (var file in files)
                    {
                        using FileStream fileStrem = File.OpenRead(file);
                        using StreamReader streamReader = new(fileStrem);
                        var items = ReadFromStream<IEnumerable<T>>(streamReader);

                        _items.AddRange(items);
                        byte[] bytes = new byte[16];
                        BitConverter.GetBytes(batch).CopyTo(bytes, 0);
                        AddFile(new Guid(bytes), file);
                        batch++;
                    }
                }
            }
            if (_items == null)
            {
                _items = new List<T>();
            }
        }

        public override void StoreChanges()
        {
            var settings = (_settings as Settings);
            if (!string.IsNullOrEmpty(Path) && System.IO.Directory.Exists(Path) && !string.IsNullOrEmpty(settings.Name))
            {
                var removedFiles = System.IO.Directory.GetFiles(Path, settings.Name).ToDictionary(x => x);
                int batch = 1;
                List<T> batchFiles = new List<T>();
                foreach (var item in _items)
                {
                    batchFiles.Add(item);
                    if(batchFiles.Count == _batchSize)
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
                        System.IO.File.Delete(kvp.Value);
                    }
                }
            }
        }

        private void SaveBatch(int batch, List<T> batchFiles, Dictionary<string, string> removedFiles)
        {
            var settings = (_settings as Settings);
            byte[] bytes = new byte[16];
            BitConverter.GetBytes(batch).CopyTo(bytes, 0);
            var guid = new Guid(bytes);
            var fileName = settings.Name.Contains("*") ? settings.Name.Replace("*", guid.ToString("D")) : $"{settings.Name}-{guid.ToString("D")}";
            var path = System.IO.Path.Combine(Path, fileName);
            AddFile(new Guid(bytes), path);

            using FileStream fileStream = File.OpenWrite(path);
            using StreamWriter streamWriter = new(fileStream);
            WriteToStream(streamWriter, batchFiles);

            if (removedFiles.ContainsKey(path))
            {
                removedFiles.Remove(path);
            }
        }
    }
}

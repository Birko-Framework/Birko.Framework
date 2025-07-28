using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.NetworkInformation;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonSeparateBulkStore<T>
        : JsonBulkStore<T>
        , ISettingsStore<ISettings>
        , ISettingsStore<Settings>
        where T : Models.AbstractModel
    {
        private Dictionary<Guid, string> _files = null;
        public JsonSeparateBulkStore() : base()
        {
            _files = new Dictionary<Guid, string>();
        }

        protected void AddFile(Guid guid, string name)
        {
            _files ??= new Dictionary<Guid, string>();
            _files[guid] = name;
        }

        public override void Init()
        {
            if (!string.IsNullOrEmpty(PathDirectory) && !Directory.Exists(PathDirectory))
            {
                if (!Directory.Exists(PathDirectory))
                {
                    Directory.CreateDirectory(PathDirectory);
                }
            }
            _files = new Dictionary<Guid, string>();
        }

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
            Directory.Delete(Path);
        }

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
                using FileStream fileStrem = File.OpenRead(file);
                var item = ReadFromStream<T>(fileStrem);
                _items.Add(item.Guid.Value, item);
                AddFile(item.Guid.Value, file);
            }
        }

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
                    var fileName = _settings.Name.Contains('*') ? _settings.Name.Replace("*", item.Key.ToString("D")) : $"{_settings.Name}-{item.Key.ToString("D")}";
                    var path = System.IO.Path.Combine(Path, fileName);
                    _files.Add(item.Key, path);

                    using FileStream fileStream = File.OpenWrite(_files[item.Key]);
                    WriteToStream(fileStream, item);
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
    }
}

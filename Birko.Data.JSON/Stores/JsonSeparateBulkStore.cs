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
            if (!string.IsNullOrEmpty(Path) && !Directory.Exists(Path))
            {
                if (!Directory.Exists(Path))
                {
                    Directory.CreateDirectory(Path);
                }
            }
            _files = new Dictionary<Guid, string>();
        }

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
                using FileStream fileStrem = File.OpenRead(file);
                var item = ReadFromStream<T>(fileStrem);
                _items.Add(item.Guid.Value, item);
                AddFile(item.Guid.Value, file);
            }
        }

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

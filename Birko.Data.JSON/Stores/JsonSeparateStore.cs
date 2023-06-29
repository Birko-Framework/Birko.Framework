using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.NetworkInformation;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonSeparateStore<T> : AbstractJsonStore<T>
        where T: Models.AbstractModel
    {
        private Dictionary<Guid, string> _files = null;
        public JsonSeparateStore(): base()
        {
            _files = new Dictionary<Guid, string>();
        }

        protected void AddFile(Guid guid, string name)
        {
            _files ??= new Dictionary<Guid, string>();
            _files[guid] = name;
        }

        public override string GetPath()
        {
            return ((_settings is Settings settings) && !string.IsNullOrEmpty(settings.Location))
                    ? settings.Location
                    : null;
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
            var settings = (_settings as Settings);
            if (!string.IsNullOrEmpty(Path) && Directory.Exists(Path) && !string.IsNullOrEmpty(settings.Name))
            {
                var files = Directory.GetFiles(Path, settings.Name).ToArray();
                if (files.Any())
                {
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
                Directory.Delete(Path);
            }
        }

        public override void Load()
        {
            var settings = (_settings as Settings);
            if (!string.IsNullOrEmpty(Path) && Directory.Exists(Path) && !string.IsNullOrEmpty(settings.Name))
            {
                var files = Directory.GetFiles(Path, settings.Name).ToArray();
                if (files.Any())
                {
                    _items = new();
                    foreach (var file in files)
                    {
                        using FileStream fileStrem = File.OpenRead(file);
                        using StreamReader streamReader = new(fileStrem);
                        var item = ReadFromStream<T>(streamReader);
                        _items.Add(item.Guid.Value, item);
                        AddFile(item.Guid.Value, file);
                    }
                }
            }
            _items ??= new();
        }

        public override void StoreChanges()
        {
            var settings = (_settings as Settings);
            if (!string.IsNullOrEmpty(Path) && Directory.Exists(Path) && !string.IsNullOrEmpty(settings.Name))
            {
                var removedFiles = Directory.GetFiles(Path, settings.Name).ToDictionary(x => x);
                foreach (var item in _items)
                {
                    if (_files.ContainsKey(item.Key))
                    {
                        var fileName = settings.Name.Contains('*') ? settings.Name.Replace("*", item.Key.ToString("D")) : $"{settings.Name}-{item.Key.ToString("D")}";
                        var path = System.IO.Path.Combine(Path, fileName);
                        _files.Add(item.Key, path);

                        using FileStream fileStream = File.OpenWrite(_files[item.Key]);
                        using StreamWriter streamWriter = new(fileStream);
                        WriteToStream(streamWriter, item);
                    }

                    if (removedFiles.ContainsKey(_files[item.Key]))
                    {
                        removedFiles.Remove(_files[item.Key]);
                    }
                }
                if (removedFiles.Any())
                {
                    foreach (var kvp in removedFiles)
                    {
                        File.Delete(kvp.Value);
                    }
                }
            }
        }
    }
}

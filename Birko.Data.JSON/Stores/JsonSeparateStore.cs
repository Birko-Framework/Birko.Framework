using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
            if (_files == null)
            {
                _files = new Dictionary<Guid, string>(); 
            }
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
            if (!string.IsNullOrEmpty(Path) && !System.IO.Directory.Exists(Path))
            {
                if (!System.IO.Directory.Exists(Path))
                {
                    System.IO.Directory.CreateDirectory(Path);
                }
            }
            _files = new Dictionary<Guid, string>();
        }

        public override void Destroy()
        {
            _items?.Clear();
            _files.Clear();
            var settings = (_settings as Settings);
            if (!string.IsNullOrEmpty(Path) && System.IO.Directory.Exists(Path) && !string.IsNullOrEmpty(settings.Name))
            {
                var files = System.IO.Directory.GetFiles(Path, settings.Name).ToArray();
                if (files.Any())
                {
                    foreach (var file in files)
                    {
                        System.IO.File.Delete(file);
                    }
                }
                System.IO.Directory.Delete(Path);
            }
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
                    foreach (var file in files)
                    {
                        var item = System.Text.Json.JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(file));
                        _items.Add(item);
                        AddFile(item.Guid.Value, file);
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
                foreach (var item in _items)
                {
                    if (_files.ContainsKey(item.Guid.Value))
                    {
                        var fileName = settings.Name.Contains("*") ? settings.Name.Replace("*", item.Guid?.ToString("D")) : $"{settings.Name}-{item.Guid?.ToString("D")}";
                        var path = System.IO.Path.Combine(Path, fileName);
                        _files.Add(item.Guid.Value, path);
                    }
                    System.IO.File.WriteAllText(_files[item.Guid.Value], System.Text.Json.JsonSerializer.Serialize(item, new System.Text.Json.JsonSerializerOptions()
                    {
                        WriteIndented = true
                    }));
                    if (removedFiles.ContainsKey(_files[item.Guid.Value]))
                    {
                        removedFiles.Remove(_files[item.Guid.Value]);
                    }
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
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonBulkStore<T> 
        : AbstractJsonBulkStore<T>
        , ISettingsStore<ISettings>
        , ISettingsStore<Settings>
        where T: Models.AbstractModel
    {

        protected Settings _settings;

        public string Path
        {
            get
            {
                return GetPath();
            }
        }

        public string PathDirectory
        {
            get
            {
                return GetDirectory();
            }
        }

        public JsonBulkStore(): base()
        {

        }

        public virtual void SetSettings(ISettings settings)
        {
            if (settings is Settings settings1)
            {
                SetSettings(settings1);
            }
        }

        public virtual void SetSettings(Settings settings)
        {
            _settings = settings;
            Init();
            LoadData();
        }

        public virtual string GetPath()
        {
            return (!string.IsNullOrEmpty(_settings?.Name))
                ? System.IO.Path.Combine(PathDirectory, _settings.Name)
                : null;
        }

        public virtual string GetDirectory()
        {
            return (!string.IsNullOrEmpty(_settings?.Location))
                ? _settings?.Location
                : null;
        }

        public override void Init()
        {
            if (!string.IsNullOrEmpty(Path) && !File.Exists(Path) && (_settings is Settings settings))
            {
                if (!Directory.Exists(PathDirectory))
                {
                    Directory.CreateDirectory(PathDirectory);
                }
                File.WriteAllText(Path, "[]");
            }
        }

        public override void Destroy()
        {
            _items?.Clear();
            if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            {
                File.Delete(Path);
            }
        }

        protected override void LoadData()
        {
            if (string.IsNullOrEmpty(Path) || !File.Exists(Path))
            {
                _items ??= new();
            }
            using FileStream fileStrem = File.OpenRead(Path);
            var items = ReadFromStream<List<T>>(fileStrem);
            _items = new();
            foreach (var item in items)
            {
                _items.Add(item.Guid.Value, item);
            }
        }

        protected override void SaveData()
        {
            if (string.IsNullOrEmpty(Path))
            {
                return;
            }
            using FileStream fileStream = File.OpenWrite(Path);
            WriteToStream(fileStream, _items);
        }
    }
}

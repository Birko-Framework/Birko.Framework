using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonStore<T> : AbstractJsonStore<T>
        where T: Models.AbstractModel
    {
        public JsonStore(): base()
        {

        }

        public override string GetPath()
        {
            return ((_settings is Settings settings) && !string.IsNullOrEmpty(settings.Location) && !string.IsNullOrEmpty(settings.Name))
                ? System.IO.Path.Combine(settings.Location, settings.Name)
                : null;
        }

        public override void Init()
        {
            if (!string.IsNullOrEmpty(Path) && !File.Exists(Path) && (_settings is Settings settings))
            {
                if (!Directory.Exists(settings.Location))
                {
                    Directory.CreateDirectory(settings.Location);
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

        public override void Load()
        {
            if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            {
                using FileStream fileStrem = File.OpenRead(Path);
                using StreamReader streamReader = new(fileStrem);
                var items = ReadFromStream<List<T>>(streamReader);
                _items = new ();
                foreach(var item in items)
                {
                    _items.Add(item.Guid.Value, item);
                }
            }
            _items ??= new();
        }

        public override void StoreChanges()
        {
            if (!string.IsNullOrEmpty(Path))
            {
                using FileStream fileStream =  File.OpenWrite(Path);
                using StreamWriter streamWriter = new(fileStream);
                WriteToStream(streamWriter,_items);
            }
        }
    }
}

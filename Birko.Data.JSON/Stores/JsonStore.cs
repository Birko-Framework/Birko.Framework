using System;
using System.Collections.Generic;
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
            if (!string.IsNullOrEmpty(Path) && !System.IO.File.Exists(Path) && (_settings is Settings settings))
            {
                if (!System.IO.Directory.Exists(settings.Location))
                {
                    System.IO.Directory.CreateDirectory(settings.Location);
                }
                System.IO.File.WriteAllText(Path, "[]");
            }
        }

        public override void Destroy()
        {
            _items?.Clear();
            if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
            {
                System.IO.File.Delete(Path);
            }
        }

        public override void Load()
        {
            if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
            {
                _items  = System.Text.Json.JsonSerializer.Deserialize<List<T>>(System.IO.File.ReadAllText(Path));
            }
            if (_items == null)
            {
                _items = new List<T>();
            }
        }

        public override void StoreChanges()
        {
            if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
            {
                System.IO.File.WriteAllText(Path, System.Text.Json.JsonSerializer.Serialize(_items, new System.Text.Json.JsonSerializerOptions() {
                    WriteIndented = true
                }));
            }
        }
    }
}

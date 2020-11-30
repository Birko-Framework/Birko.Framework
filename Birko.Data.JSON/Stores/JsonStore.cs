using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public class JsonStore<T> : AbstractStore<T>
        where T: Models.AbstractModel
    {
        private ISettings _settings;
        private List<T> _items = new List<T>();

        public string Path
        {
            get
            {
                return ((_settings is Settings settings) && !string.IsNullOrEmpty(settings.Location) && !string.IsNullOrEmpty(settings.Name))
                    ? System.IO.Path.Combine(settings.Location, settings.Name)
                    : null;
            }
        }

        public JsonStore()
        {

        }

        public override void SetSettings(ISettings settings)
        {
            if (settings is Settings setts)
            {
                _settings = setts;
                Init();
                Load();
            }
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

        public override void List(Action<T> action)
        {
            List(null, action);
        }

        public override void List(Expression<Func<T, bool>> filter, Action<T> action, int? limit = null, int? offset = null)
        {
            if(_items != null && _items.Any() && action != null)
            {
                var items = _items.ToArray<T>();
                if (filter != null)
                {
                    items = items.Where(filter.Compile()).ToArray();
                }
                if (offset != null && offset > 0)
                {
                    items = items.Skip(offset.Value).ToArray();
                }
                if (limit != null)
                {
                    items = items.Take(limit.Value).ToArray();
                }
                foreach (T item in items)
                {
                    action?.Invoke(item);
                }
            }
        }


        public override long Count(Expression<Func<T, bool>> filter)
        {

            return (filter != null)
                ?_items?.Where(filter.Compile()).Count() ?? 0
                : _items?.Count ?? 0;
        }

        public override void Save(T data, StoreDataDelegate<T> storeDelegate = null)
        {
            if (data != null)
            {
                bool newItem = data.Guid == null;
                if (newItem) // new
                {
                    data.Guid = Guid.NewGuid();
                }
                data = storeDelegate?.Invoke(data) ?? data;
                if (data != null)
                {
                    if (newItem) // new
                    {
                        _items.Add(data);
                    }
                    else //update
                    {
                        if (data is Models.AbstractLogModel)
                        {
                            (data as Models.AbstractLogModel).PrevUpdatedAt = (data as Models.AbstractLogModel).UpdatedAt;
                            (data as Models.AbstractLogModel).UpdatedAt = DateTime.UtcNow;
                        }
                        var item = _items.FirstOrDefault(x => x.Guid == data.Guid);
                        System.Reflection.MethodInfo method = typeof(T).GetMethod("CopyTo", new[] { typeof(T) });
                        method.Invoke(data, new[] { item });
                    }
                }
            }
        }

        public override void Delete(T data)
        {
            if (data.Guid != null && _items != null && _items.Any(x => x.Guid == data.Guid))
            {
                var item = _items.FirstOrDefault(x => x.Guid == data.Guid);
                _items.Remove(item);
            }
        }

        public void Load()
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

        public T First()
        {
            return (_items?.Any() == true) ? _items.FirstOrDefault() : null;
        }
    }
}

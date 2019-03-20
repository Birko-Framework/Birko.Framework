using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Store
{
    public class JsonStore<T> : IStore<T>
        where T: Model.AbstractModel
    {
        private readonly Settings _settings;
        private List<T> _items = new List<T>();

        public string Path
        {
            get
            {
                return (!string.IsNullOrEmpty(_settings?.Location) && !string.IsNullOrEmpty(_settings?.Name))
                    ? System.IO.Path.Combine(_settings.Location, _settings.Name)
                    : null;
            }
        }

        public JsonStore(Settings settings)
        {
            _settings = settings;
            Init();
            Load();
        }

        public void Init()
        {
            if (!string.IsNullOrEmpty(Path) && !System.IO.File.Exists(Path))
            {
                System.IO.File.WriteAllText(Path, "[]");
            }
        }

        public void Destroy()
        {
            if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
            {
                System.IO.File.Delete(Path);
            }
        }

        public void List(Action<T> action)
        {
            List(null, action);
        }

        public void List(Expression<Func<T, bool>> filter, Action<T> action)
        {
            if(_items != null && _items.Any() && action != null)
            {
                var items = _items.ToArray<T>();
                if (filter != null)
                {
                    items = items.Where(filter.Compile()).ToArray();
                }
                foreach (T item in items)
                {
                    action?.Invoke(item);
                }
            }
        }

        public long Count()
        {
            return _items?.Count ?? 0;
        }


        public long Count(Expression<Func<T, bool>> filter)
        {
            return _items?.Where(filter.Compile()).Count() ?? 0;
        }

        public void Save(T data, StoreDataDelegate<T> storeDelegate = null)
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
                        if (data is Model.AbstractLogModel)
                        {
                            (data as Model.AbstractLogModel).PrevUpdatedAt = (data as Model.AbstractLogModel).UpdatedAt;
                            (data as Model.AbstractLogModel).UpdatedAt = DateTime.UtcNow;
                        }
                        var item = _items.FirstOrDefault(x => x.Guid == data.Guid);
                        System.Reflection.MethodInfo method = typeof(T).GetMethod("CopyTo", new[] { typeof(T) });
                        method.Invoke(data, new[] { item });
                    }
                }
            }
        }

        public void Delete(T data)
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
                using (System.IO.StreamReader file = System.IO.File.OpenText(Path))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    _items = (List<T>)serializer.Deserialize(file, typeof(List<T>));
                }
            }
            if (_items == null)
            {
                _items = new List<T>();
            }
        }

        public void StoreChanges()
        {
            if (!string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path))
            {
                using (System.IO.TextWriter file = System.IO.File.CreateText(Path))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, _items);
                }
            }
        }
    }
}

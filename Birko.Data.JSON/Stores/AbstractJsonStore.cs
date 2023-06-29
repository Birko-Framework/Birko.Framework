using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public abstract class AbstractJsonStore<T> : AbstractStore<T>
        where T: Models.AbstractModel
    {
        protected ISettings _settings;
        protected Dictionary<Guid, T> _items = new ();

        public string Path
        {
            get
            {
                return GetPath();
            }
        }

        public AbstractJsonStore()
        {

        }

        public abstract string GetPath();
        public override void SetSettings(ISettings settings)
        {
            if (settings is Settings setts)
            {
                _settings = setts;
                Init();
                Load();
            }
        }

        public abstract void Load();

        public override void List(Action<T> action)
        {
            List(null, action);
        }

        public override void List(Expression<Func<T, bool>> filter, Action<T> action, int? limit = null, int? offset = null)
        {
            if((_items?.Any() ?? false) && action != null)
            {
                int i = 0;
                foreach (var item in _items.Where(x =>  filter?.Compile()?.Invoke(x.Value) ?? true))
                {
                    if(i < (offset ?? 0)) 
                    {
                        continue;
                    }
                    if(i >= (limit ?? _items.Count)) 
                    {
                        break;
                    }
                    action?.Invoke(item.Value);
                    i++;
                }
            }
        }


        public override long Count(Expression<Func<T, bool>> filter)
        {
            return _items?.Where(x =>  filter?.Compile()?.Invoke(x.Value) ?? true)?.Count() ?? 0;
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
                        _items.Add(data.Guid.Value, data);
                    }
                    else //update
                    {
                        if (data is Models.AbstractLogModel)
                        {
                            (data as Models.AbstractLogModel).PrevUpdatedAt = (data as Models.AbstractLogModel).UpdatedAt;
                            (data as Models.AbstractLogModel).UpdatedAt = DateTime.UtcNow;
                        }
                        System.Reflection.MethodInfo method = typeof(T).GetMethod("CopyTo", new[] { typeof(T) });
                        method.Invoke(data, new[] { _items[data.Guid.Value] });
                    }
                }
            }
        }

        public override void Delete(T data)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                _items.Remove(data.Guid.Value);
            }
        }

        public T First()
        {
            return _items?.FirstOrDefault().Value ?? null;
        }

        protected TData ReadFromStream<TData>(StreamReader streamReader)
        {
            using JsonReader jsonReader = new JsonTextReader(streamReader);
            JsonSerializer serializer = new();
            return serializer.Deserialize<TData>(jsonReader);
        }
        protected void WriteToStream<TData>(StreamWriter streamWriter, TData data)
        {
            using JsonWriter jsonWriter = new JsonTextWriter(streamWriter);
            jsonWriter.Formatting = Formatting.Indented;
            JsonSerializer serializer = new();
            serializer.Serialize(jsonWriter, data);
        }
    }
}

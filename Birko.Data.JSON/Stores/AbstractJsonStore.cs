using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;

namespace Birko.Data.Stores
{
    public abstract class AbstractJsonStore<T> : AbstractStore<T>
        where T: Models.AbstractModel
    {
      
        protected Dictionary<Guid, T> _items = new ();
        public AbstractJsonStore()
        {

        }

        public override T? ReadOne(Expression<Func<T, bool>>? filter = null)
        {
            return _items?.Values.Where(x => filter?.Compile()?.Invoke(x) ?? true)?.FirstOrDefault() ?? null;
        }

        protected abstract void LoadData();

        protected abstract void SaveData();

        public override long Count(Expression<Func<T, bool>>? filter = null)
        {
            return _items?.Where(x => filter?.Compile()?.Invoke(x.Value) ?? true)?.Count() ?? 0;
        }

        public override void Create(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            data.Guid = Guid.NewGuid(); 
            storeDelegate?.Invoke(data);
            _items.Add(data.Guid.Value, data);
            SaveData();
        }

        public override void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                storeDelegate?.Invoke(data);
                _items[data.Guid.Value] = data;
                SaveData();
            }
        }

        public override void Delete(T data)
        {
            if (data.Guid != null && (_items?.ContainsKey(data.Guid.Value) ?? false))
            {
                _items.Remove(data.Guid.Value);
                SaveData();
            }
        }

        protected static TData ReadFromStream<TData>(FileStream stream)
        {
            return JsonSerializer.Deserialize<TData>(stream);
        }

        protected static void WriteToStream<TData>(FileStream stream, TData data)
        {
            using Utf8JsonWriter jsonWriter = new(stream, new JsonWriterOptions() { 
                Indented = true,
            });
            JsonSerializer.Serialize(jsonWriter, data);
        }

        /*
        public abstract string GetPath();
        
       

        public override IEnumerable<T>(Action<T> action)
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
        */
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Stores
{
    public abstract class AbstractJsonBulkStore<T>
        : AbstractJsonStore<T>
        , IBulkStore<T>
        where T: Models.AbstractModel
    {
      
        public AbstractJsonBulkStore() : base()
        {

        }

        public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, int? limit = null, int? offset = null)
        {
            var result = _items?.Values.Where(x => filter?.Compile()?.Invoke(x) ?? true);
            if(offset != null)
            {
                result = result?.Skip(offset.Value);
            }
            if (limit != null)
            {
                result = result?.Take(limit.Value);
            }
            return result;
        }

        public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            bool save = false;
            foreach (var item in data.Where(x => x != null))
            {
                item.Guid = Guid.NewGuid();
                storeDelegate?.Invoke(item);
                _items.Add(item.Guid.Value, item);
                save = true;
            }
            if (save)
            {
                SaveData();
            }
        }

        public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            bool save = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && (_items?.ContainsKey(item.Guid.Value) ?? false))
                {
                    storeDelegate?.Invoke(item);
                    _items[item.Guid.Value] = item;
                    save = true;
                }
            }
            if (save)
            {
                SaveData();
            }
        }

        public void Delete(IEnumerable<T> data)
        {
            bool save = false;
            foreach (var item in data.Where(x => x != null))
            {
                if (item.Guid != null && (_items?.ContainsKey(item.Guid.Value) ?? false))
                {
                    _items.Remove(item.Guid.Value);
                    save = true;
                }
            }
            if (save)
            {
                SaveData();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonRepository<TViewModel, TModel> : AbstractRepository<TViewModel, TModel>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        public JsonRepository(string path, string name): base(path)
        {
            _store = new Stores.JsonStore<TModel>(new Stores.Settings()
            {
                Location = path,
                Name = name
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repository
{
    public abstract class JsonRepository<TViewModel, TModel> : AbstractRepository<TViewModel, TModel>
        where TModel:Model.AbstractModel, Model.ILoadable<TViewModel>
        where TViewModel:Model.ILoadable<TModel>
    {
        public JsonRepository(string path, string name): base(path)
        {
            _store = new Store.JsonStore<TModel>(new Store.Settings()
            {
                Location = path,
                Name = name
            });
        }
    }
}

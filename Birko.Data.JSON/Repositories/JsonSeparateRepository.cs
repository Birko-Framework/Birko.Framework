using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonSeparateRepository<TViewModel, TModel> : AbstractJsonSeparateRepository<TViewModel, TModel, Stores.JsonSeparateStore<TModel>>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        public JsonSeparateRepository() : base()
        {

        }
    }
}

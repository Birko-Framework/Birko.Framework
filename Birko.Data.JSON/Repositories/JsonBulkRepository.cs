using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonBulkRepository<TViewModel, TModel> : AbstractJsonBulkRepository<TViewModel, TModel, Stores.JsonBulkStore<TModel>>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        public JsonBulkRepository() : base()
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonSeparateBulkRepository<TViewModel, TModel> : AbstractJsonBulkRepository<TViewModel, TModel, Stores.JsonSeparateBulkStore<TModel>>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        public JsonSeparateBulkRepository() : base()
        {

        }
    }
}

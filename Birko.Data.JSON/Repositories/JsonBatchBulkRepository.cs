using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonBatchBulkRepository<TViewModel, TModel> : AbstractJsonBulkRepository<TViewModel, TModel, Stores.JsonBatchBulkStore<TModel>>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        public JsonBatchBulkRepository() : base()
        {

        }
    }
}

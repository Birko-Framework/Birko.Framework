using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonBatchRepository<TViewModel, TModel> : AbstractJsonRepository<TViewModel, TModel, Stores.JsonBatchStore<TModel>>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        public JsonBatchRepository() : base()
        {

        }
    }
}

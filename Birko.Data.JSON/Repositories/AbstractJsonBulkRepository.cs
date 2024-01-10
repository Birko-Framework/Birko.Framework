using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class AbstractJsonBulkRepository<TViewModel, TModel, TStore> : AbstractBulkStoreRepository<TViewModel, TModel>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
        where TStore : Stores.AbstractJsonBulkStore<TModel>
    {
        public AbstractJsonBulkRepository() : base()
        {

        }
    }
}

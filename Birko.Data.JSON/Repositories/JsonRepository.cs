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
        public JsonRepository() : base()
        {

        }

        public override void SetSettings(Stores.ISettings settings)
        {
            if (settings is Stores.Settings setts)
            {
                base.SetSettings(setts);
                Store = Stores.StoreLocator.GetStore<Stores.JsonStore<TModel>, Stores.ISettings>(setts);
            }
        }
    }
}

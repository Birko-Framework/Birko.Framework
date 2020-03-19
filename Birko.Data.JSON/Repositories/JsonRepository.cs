using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Repositories
{
    public abstract class JsonRepository<TViewModel, TModel> : AbstractRepository<TViewModel, TModel, Stores.Settings>
        where TModel:Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel:Models.ILoadable<TModel>
    {
        private Stores.JsonStore<TModel> _store = null;

        public JsonRepository() : base()
        {

        }

        protected override Stores.IStore<TModel, Stores.Settings> GetStore()
        {
            return _store;
        }

        public override void SetSettings(Stores.Settings settings)
        {
            base.SetSettings(settings);
            _store = Stores.StoreLocator.GetStore<Stores.JsonStore<TModel>, Stores.Settings>(settings);
        }
    }
}

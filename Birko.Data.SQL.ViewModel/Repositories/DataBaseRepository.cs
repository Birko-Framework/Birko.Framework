using Birko.Data.Repositories;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.SQL.Repositories
{
    public abstract class DataBaseRepository<TConnector, TViewModel, TModel>
        : AbstractBulkViewModelRepository<TViewModel, TModel>
        , IDataBaseRepository<TConnector, TViewModel, TModel>
        where TConnector : SQL.Connectors.AbstractConnector
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the database connector from the (potentially wrapped) store.
        /// This works with tenant wrappers and other store wrappers.
        /// </summary>
        public TConnector? Connector => Store?.GetUnwrappedStore<TModel, DataBaseBulkStore<TConnector, TModel>>()?.Connector;

        public DataBaseRepository()
            : this(new DataBaseBulkStore<TConnector, TModel>())
        {
        }

        public DataBaseRepository(IStore<TModel>? store) : base(null)
        {
            // SH-M208 (TASK-162): IsStoreOfType inspects the INNERMOST store while every inherited bulk method tests the
            // OUTERMOST one, so a non-bulk wrapper (TenantStoreWrapper) constructed fine and every bulk call then threw.
            if (store != null && store is not IBulkStore<TModel>)
            {
                throw new ArgumentException(
                    $"Store must be a bulk store; {store.GetType().Name} is not. Wrap with the bulk wrapper (AsTenantAware picks it).",
                    nameof(store));
            }
            if (store != null && !store.IsStoreOfType<TModel, DataBaseBulkStore<TConnector, TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type DataBaseBulkStore<TConnector, TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                    nameof(store));
            }
            // Set the store after validation - base constructor handles null by creating default
            if (store != null)
            {
                Store = store;
            }
        }

        public virtual void AddOnInit(SQL.Connectors.InitConnector onInit)
        {
            if (Store != null && onInit != null)
            {
                var innerStore = Store.GetUnwrappedStore<TModel, DataBaseBulkStore<TConnector, TModel>>();
                innerStore?.AddOnInit(onInit);
            }
        }

        public virtual void RemoveOnInit(SQL.Connectors.InitConnector onInit)
        {
            if (Store != null && onInit != null)
            {
                var innerStore = Store.GetUnwrappedStore<TModel, DataBaseBulkStore<TConnector, TModel>>();
                innerStore?.RemoveOnInit(onInit);
            }
        }
    }
}

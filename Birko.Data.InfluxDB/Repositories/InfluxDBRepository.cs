using Birko.Data.Stores;
using System;

namespace Birko.Data.InfluxDB.Repositories
{
    /// <summary>
    /// Synchronous InfluxDB repository with bulk operations support.
    /// Inherits from AbstractBulkRepository to provide bulk operations via InfluxDB's write API.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class InfluxDBRepository<TViewModel, TModel>
        : Data.Repositories.AbstractBulkViewModelRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel, Data.Models.ILoadable<TViewModel>
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the InfluxDB bulk store.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public Stores.InfluxDBStore<TModel>? InfluxDBStore => Store?.GetUnwrappedStore<TModel, Stores.InfluxDBStore<TModel>>();

        /// <summary>
        /// Initializes a new instance of the InfluxDBRepository class.
        /// </summary>
        public InfluxDBRepository()
            : base(null)
        {
            Store = new Stores.InfluxDBStore<TModel>();
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The InfluxDB bulk store to use (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public InfluxDBRepository(Data.Stores.IStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, Stores.InfluxDBStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type InfluxDBStore<TModel> or a wrapper around it (e.g., TenantStoreWrapper).",
                    nameof(store));
            }
            Store = store ?? new Stores.InfluxDBStore<TModel>();
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The InfluxDB settings to use.</param>
        public void SetSettings(Stores.Settings settings)
        {
            if (settings != null && InfluxDBStore != null)
            {
                InfluxDBStore.SetSettings(settings);
            }
        }

        /// <summary>
        /// Checks if the InfluxDB server is healthy.
        /// </summary>
        /// <returns>True if the server is reachable, false otherwise.</returns>
        public bool IsHealthy()
        {
            return InfluxDBStore?.Client?.IsHealthy() ?? false;
        }

        /// <summary>
        /// Drops the InfluxDB bucket for this repository.
        /// </summary>
        public void Drop()
        {
            InfluxDBStore?.Destroy();
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            base.Destroy();
            Drop();
        }
    }
}

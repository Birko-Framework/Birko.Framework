using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.InfluxDB.Repositories
{
    /// <summary>
    /// Async InfluxDB repository with bulk operations support.
    /// Inherits from AbstractAsyncBulkRepository to provide bulk operations via InfluxDB's async write API.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AsyncInfluxDBRepository<TViewModel, TModel>
        : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the InfluxDB async store with bulk operations support.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public Stores.AsyncInfluxDBStore<TModel>? InfluxDBStore => Store?.GetUnwrappedStore<TModel, Stores.AsyncInfluxDBStore<TModel>>();

        /// <summary>
        /// Initializes a new instance of the AsyncInfluxDBRepository class.
        /// </summary>
        public AsyncInfluxDBRepository()
            : base(null)
        {
            Store = new Stores.AsyncInfluxDBStore<TModel>();
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async InfluxDB store to use for both regular and bulk operations (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncInfluxDBRepository(Data.Stores.IAsyncStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, Stores.AsyncInfluxDBStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncInfluxDBStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                    nameof(store));
            }
            Store = store ?? new Stores.AsyncInfluxDBStore<TModel>();
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
        /// <param name="ct">Cancellation token.</param>
        public async Task DropAsync(CancellationToken ct = default)
        {
            if (InfluxDBStore != null)
            {
                await InfluxDBStore.DestroyAsync(ct);
            }
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            await base.DestroyAsync(ct);
            if (InfluxDBStore != null)
            {
                await DropAsync(ct);
            }
        }
    }
}

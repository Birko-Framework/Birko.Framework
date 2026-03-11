using Birko.Data.Stores;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.InfluxDB.Repositories
{
    /// <summary>
    /// Async InfluxDB repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncInfluxDBModelRepository<T>
        : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Gets the InfluxDB async store.
        /// </summary>
        public Stores.AsyncInfluxDBStore<T>? InfluxDBStore => Store?.GetUnwrappedStore<T, Stores.AsyncInfluxDBStore<T>>();

        public AsyncInfluxDBModelRepository()
            : base(null)
        {
            Store = new Stores.AsyncInfluxDBStore<T>();
        }

        public AsyncInfluxDBModelRepository(Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, Stores.AsyncInfluxDBStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncInfluxDBStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new Stores.AsyncInfluxDBStore<T>();
        }

        public void SetSettings(Stores.Settings settings)
        {
            if (settings != null && InfluxDBStore != null)
            {
                InfluxDBStore.SetSettings(settings);
            }
        }

        public bool IsHealthy()
        {
            return InfluxDBStore?.Client?.IsHealthy() ?? false;
        }

        public async Task DropAsync(CancellationToken ct = default)
        {
            if (InfluxDBStore != null)
            {
                await InfluxDBStore.DestroyAsync(ct);
            }
        }

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

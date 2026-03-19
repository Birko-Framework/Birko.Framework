using Birko.Data.Stores;
using Birko.Configuration;
using System;

namespace Birko.Data.InfluxDB.Repositories
{
    /// <summary>
    /// Synchronous InfluxDB repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class InfluxDBModelRepository<T>
        : Data.Repositories.AbstractBulkRepository<T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Gets the InfluxDB bulk store.
        /// </summary>
        public Stores.InfluxDBStore<T>? InfluxDBStore => Store?.GetUnwrappedStore<T, Stores.InfluxDBStore<T>>();

        public InfluxDBModelRepository()
            : base(null)
        {
            Store = new Stores.InfluxDBStore<T>();
        }

        public InfluxDBModelRepository(Data.Stores.IStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, Stores.InfluxDBStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type InfluxDBStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new Stores.InfluxDBStore<T>();
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

        public void Drop()
        {
            InfluxDBStore?.Destroy();
        }

        public override void Destroy()
        {
            base.Destroy();
            Drop();
        }
    }
}

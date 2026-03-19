using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.TimescaleDB.Stores;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async TimescaleDB repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncTimescaleDBModelRepository<T>
        : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the TimescaleDB connector.
        /// </summary>
        public TimescaleDBConnector? Connector => Store?.GetUnwrappedStore<T, AsyncTimescaleDBStore<T>>()?.Connector;

        public AsyncTimescaleDBModelRepository()
            : base(null)
        {
            Store = new AsyncTimescaleDBStore<T>();
        }

        public AsyncTimescaleDBModelRepository(Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncTimescaleDBStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncTimescaleDBStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new AsyncTimescaleDBStore<T>();
        }

        public void SetSettings(TimescaleDBSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<T, AsyncTimescaleDBStore<T>>();
                innerStore?.SetSettings(settings);
            }
        }

        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<T, AsyncTimescaleDBStore<T>>();
                innerStore?.SetSettings(settings);
            }
        }

        public void SetSettings(PasswordSettings settings)
        {
            if (settings is RemoteSettings remote)
            {
                SetSettings(remote);
            }
        }

        public async Task InitAsync(CancellationToken ct = default)
        {
            if (Connector == null)
                throw new InvalidOperationException("Connector not initialized. Call SetSettings() first.");
            await Task.Run(() => Connector.DoInit(), ct).ConfigureAwait(false);
        }

        public async Task DropAsync(CancellationToken ct = default)
        {
            if (Connector == null)
                throw new InvalidOperationException("Connector not initialized.");
            await Task.Run(() => Connector.DropTable(new[] { typeof(T) }), ct).ConfigureAwait(false);
        }

        public async Task CreateSchemaAsync(CancellationToken ct = default)
        {
            if (Connector == null)
                throw new InvalidOperationException("Connector not initialized.");
            await Task.Run(() => Connector.CreateTable(new[] { typeof(T) }), ct).ConfigureAwait(false);
        }

        public async Task CreateHypertableAsync(string timeColumn, string chunkTimeInterval = "7 days", CancellationToken ct = default)
        {
            if (Connector == null)
                throw new InvalidOperationException("Connector not initialized.");
            await Connector.CreateHypertableAsync(typeof(T), timeColumn, chunkTimeInterval, ct).ConfigureAwait(false);
        }

        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            await base.DestroyAsync(ct);
            await DropAsync(ct);
        }
    }
}

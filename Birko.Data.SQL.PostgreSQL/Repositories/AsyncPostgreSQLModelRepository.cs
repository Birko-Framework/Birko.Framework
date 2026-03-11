using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using System;
using PasswordSettings = Birko.Data.Stores.PasswordSettings;
using RemoteSettings = Birko.Data.Stores.RemoteSettings;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async PostgreSQL repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncPostgreSQLModelRepository<T>
        : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the PostgreSQL connector.
        /// </summary>
        public PostgreSQLConnector? Connector => Store?.GetUnwrappedStore<T, AsyncPostgreSQLStore<T>>()?.Connector;

        public AsyncPostgreSQLModelRepository()
            : base(null)
        {
            Store = new AsyncPostgreSQLStore<T>();
        }

        public AsyncPostgreSQLModelRepository(Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncPostgreSQLStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncPostgreSQLStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new AsyncPostgreSQLStore<T>();
        }

        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<T, AsyncPostgreSQLStore<T>>();
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

        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            await base.DestroyAsync(ct);
            await DropAsync(ct);
        }
    }
}

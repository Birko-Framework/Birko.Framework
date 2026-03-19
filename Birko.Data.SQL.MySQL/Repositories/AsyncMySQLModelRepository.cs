using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.Stores;
using System;
using System.Threading;
using System.Threading.Tasks;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async MySQL repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncMySQLModelRepository<T>
        : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the MySQL connector.
        /// </summary>
        public MySQLConnector? Connector => Store?.GetUnwrappedStore<T, AsyncMySQLStore<T>>()?.Connector;

        public AsyncMySQLModelRepository()
            : base(null)
        {
            Store = new AsyncMySQLStore<T>();
        }

        public AsyncMySQLModelRepository(Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncMySQLStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncMySQLStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new AsyncMySQLStore<T>();
        }

        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<T, AsyncMySQLStore<T>>();
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

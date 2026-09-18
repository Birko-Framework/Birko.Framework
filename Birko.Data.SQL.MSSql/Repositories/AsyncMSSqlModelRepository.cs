using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async MSSQL repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class AsyncMSSqlModelRepository<T>
        : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the MSSQL connector.
        /// </summary>
        public MSSqlConnector? Connector => Store?.GetUnwrappedStore<T, AsyncMSSqlStore<T>>()?.Connector;

        public AsyncMSSqlModelRepository()
            : base(null)
        {
            Store = new AsyncMSSqlStore<T>();
        }

        public AsyncMSSqlModelRepository(Data.Stores.IAsyncStore<T>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<T, AsyncMSSqlStore<T>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncMSSqlStore<T> or a wrapper around it.",
                    nameof(store));
            }
            Store = store ?? new AsyncMSSqlStore<T>();
        }

        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<T, AsyncMSSqlStore<T>>();
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

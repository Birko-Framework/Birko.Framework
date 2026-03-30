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
    /// Async MSSQL repository with native async database operations and bulk support.
    /// Uses AsyncMSSqlStore which provides native SqlBulkCopy bulk operations.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AsyncMSSqlRepository<TViewModel, TModel>
        : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the MSSQL connector.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public MSSqlConnector? Connector => Store?.GetUnwrappedStore<TModel, AsyncMSSqlStore<TModel>>()?.Connector;

        /// <summary>
        /// Initializes a new instance of the AsyncMSSqlRepository class.
        /// </summary>
        public AsyncMSSqlRepository()
            : base(null)
        {
            Store = new AsyncMSSqlStore<TModel>();
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async MSSQL store to use (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncMSSqlRepository(Data.Stores.IAsyncStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, AsyncMSSqlStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncMSSqlStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                    nameof(store));
            }
            Store = store ?? new AsyncMSSqlStore<TModel>();
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The remote settings to use.</param>
        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<TModel, AsyncMSSqlStore<TModel>>();
                innerStore?.SetSettings(settings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The password settings to use.</param>
        public void SetSettings(PasswordSettings settings)
        {
            if (settings is RemoteSettings remote)
            {
                SetSettings(remote);
            }
        }

        /// <summary>
        /// Initializes the repository and creates the database schema if needed.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async Task InitAsync(CancellationToken ct = default)
        {
            if (Connector == null)
            {
                throw new InvalidOperationException("Connector not initialized. Call SetSettings() first.");
            }

            await Task.Run(() => Connector.DoInit(), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the database schema.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async Task DropAsync(CancellationToken ct = default)
        {
            if (Connector == null)
            {
                throw new InvalidOperationException("Connector not initialized.");
            }

            await Task.Run(() => Connector.DropTable(new[] { typeof(TModel) }), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the database schema for the model type.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async Task CreateSchemaAsync(CancellationToken ct = default)
        {
            if (Connector == null)
            {
                throw new InvalidOperationException("Connector not initialized.");
            }

            await Task.Run(() => Connector.CreateTable(new[] { typeof(TModel) }), ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            await base.DestroyAsync(ct);
            await DropAsync(ct);
        }
    }
}

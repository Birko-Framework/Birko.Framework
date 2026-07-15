using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.SQL.TimescaleDB.Stores;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// Async TimescaleDB repository with native async database operations and bulk support.
    /// Uses AsyncTimescaleDBStore which provides bulk operations via transactions.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AsyncTimescaleDBRepository<TViewModel, TModel>
        : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Gets the TimescaleDB connector.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public TimescaleDBConnector? Connector => Store?.GetUnwrappedStore<TModel, AsyncTimescaleDBStore<TModel>>()?.Connector;

        /// <summary>
        /// Initializes a new instance of the AsyncTimescaleDBRepository class.
        /// </summary>
        public AsyncTimescaleDBRepository()
            : base(null)
        {
            Store = new AsyncTimescaleDBStore<TModel>();
        }

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async TimescaleDB store to use (optional). Can be wrapped (e.g., by tenant wrappers).</param>
        public AsyncTimescaleDBRepository(Data.Stores.IAsyncStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, AsyncTimescaleDBStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncTimescaleDBStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                    nameof(store));
            }
            Store = store ?? new AsyncTimescaleDBStore<TModel>();
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The TimescaleDB settings to use.</param>
        public void SetSettings(TimescaleDBSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<TModel, AsyncTimescaleDBStore<TModel>>();
                innerStore?.SetSettings(settings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The remote settings to use.</param>
        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var innerStore = Store?.GetUnwrappedStore<TModel, AsyncTimescaleDBStore<TModel>>();
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
        /// Returns the connector or throws a clear error when settings were never applied.
        /// CR-L235: captures the property ONCE per call — each Connector read re-walks the (possibly
        /// wrapped) store unwrap chain, so the old guard-then-use pattern traversed it twice.
        /// </summary>
        /// <remarks>
        /// CR-L236 (accepted): InitAsync/DropAsync/CreateSchemaAsync wrap the connector's synchronous
        /// DoInit/DropTable/CreateTable in Task.Run — the CancellationToken only cancels the work
        /// before it starts; an in-flight DB call is not interrupted. CreateHypertableAsync flows the
        /// token into a genuinely async connector method. If the connector grows async overloads,
        /// prefer those over Task.Run.
        /// </remarks>
        private TimescaleDBConnector RequireConnector()
            => Connector ?? throw new InvalidOperationException("Connector not initialized. Call SetSettings() first.");

        /// <summary>
        /// Initializes the repository and creates the database schema if needed.
        /// </summary>
        /// <param name="ct">Cancellation token — observed only before the operation starts; the
        /// underlying connector call is synchronous (CR-L236).</param>
        public async Task InitAsync(CancellationToken ct = default)
        {
            var connector = RequireConnector();
            await Task.Run(() => connector.DoInit(), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the database schema.
        /// </summary>
        /// <param name="ct">Cancellation token — observed only before the operation starts; the
        /// underlying connector call is synchronous (CR-L236).</param>
        public async Task DropAsync(CancellationToken ct = default)
        {
            var connector = RequireConnector();
            await Task.Run(() => connector.DropTable(new[] { typeof(TModel) }), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the database schema for the model type.
        /// </summary>
        /// <param name="ct">Cancellation token — observed only before the operation starts; the
        /// underlying connector call is synchronous (CR-L236).</param>
        public async Task CreateSchemaAsync(CancellationToken ct = default)
        {
            var connector = RequireConnector();
            await Task.Run(() => connector.CreateTable(new[] { typeof(TModel) }), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a hypertable for the model type.
        /// This should be called after CreateSchemaAsync.
        /// </summary>
        /// <param name="timeColumn">The time column to partition by.</param>
        /// <param name="chunkTimeInterval">The chunk time interval (e.g. "7 days").</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task CreateHypertableAsync(string timeColumn, string chunkTimeInterval = "7 days", CancellationToken ct = default)
        {
            await RequireConnector().CreateHypertableAsync(typeof(TModel), timeColumn, chunkTimeInterval, ct).ConfigureAwait(false);
        }

        // CR-L234: the DestroyAsync override (base.DestroyAsync + DropAsync) was removed — the base
        // already destroys through the store, and AsyncDataBaseStore.DestroyAsync IS a table drop, so
        // the override dropped the table a second time via the unwrapped connector (bypassing any
        // wrapper). Same double-destroy pattern removed from the MongoDB.ViewModel repos (CR-L155/L156).
        // DropAsync stays as the explicit schema-drop helper.
    }
}

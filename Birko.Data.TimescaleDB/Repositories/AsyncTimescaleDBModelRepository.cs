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

        /// <summary>
        /// Returns the connector or throws a clear error when settings were never applied.
        /// CR-L233: one guard instead of the copy-pasted null-check in every schema method.
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

        public async Task InitAsync(CancellationToken ct = default)
        {
            var connector = RequireConnector();
            await Task.Run(() => connector.DoInit(), ct).ConfigureAwait(false);
        }

        public async Task DropAsync(CancellationToken ct = default)
        {
            var connector = RequireConnector();
            await Task.Run(() => connector.DropTable(new[] { typeof(T) }), ct).ConfigureAwait(false);
        }

        public async Task CreateSchemaAsync(CancellationToken ct = default)
        {
            var connector = RequireConnector();
            await Task.Run(() => connector.CreateTable(new[] { typeof(T) }), ct).ConfigureAwait(false);
        }

        public async Task CreateHypertableAsync(string timeColumn, string chunkTimeInterval = "7 days", CancellationToken ct = default)
        {
            await RequireConnector().CreateHypertableAsync(typeof(T), timeColumn, chunkTimeInterval, ct).ConfigureAwait(false);
        }

        // CR-L234 (same-defect extra): the DestroyAsync override (base.DestroyAsync + DropAsync) was
        // removed — the base already destroys through the store, and AsyncDataBaseStore.DestroyAsync
        // IS a table drop, so the override dropped the table a second time via the unwrapped
        // connector (bypassing any wrapper). DropAsync stays as the explicit schema-drop helper.
    }
}

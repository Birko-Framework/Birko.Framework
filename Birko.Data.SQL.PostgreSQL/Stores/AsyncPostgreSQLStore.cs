using Birko.Data.SQL.Connectors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Native async PostgreSQL store with bulk operation support.
    /// Combines single-item and bulk async CRUD operations in one store.
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class AsyncPostgreSQLStore<T> : AsyncDataBaseBulkStore<PostgreSQLConnector, T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Initializes a new instance of the AsyncPostgreSQLStore class.
        /// </summary>
        public AsyncPostgreSQLStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The remote settings to use.</param>
        public void SetSettings(Stores.RemoteSettings settings)
        {
            if (settings != null)
            {
                base.SetSettings((Stores.ISettings)settings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The password settings to use.</param>
        public override void SetSettings(Stores.PasswordSettings settings)
        {
            if (settings is Stores.RemoteSettings remote)
            {
                SetSettings(remote);
            }
            else
            {
                base.SetSettings(settings);
            }
        }

        /// <summary>
        /// Creates the database schema.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async Task CreateSchemaAsync(CancellationToken ct = default)
        {
            if (Connector == null)
            {
                throw new InvalidOperationException("Connector not initialized. Call SetSettings() first.");
            }

            await Task.Run(() => Connector.CreateTable(new[] { typeof(T) }), ct).ConfigureAwait(false);
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

            await Task.Run(() => Connector.DropTable(new[] { typeof(T) }), ct).ConfigureAwait(false);
        }

        #region Native Bulk Operations

        /// <inheritdoc />
        public override async Task CreateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            var items = data.ToList();
            foreach (var item in items)
            {
                item.Guid = Guid.NewGuid();
                storeDelegate?.Invoke(item);
            }

            await Connector.BulkInsertAsync(typeof(T), items.Cast<object>(), ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task UpdateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            var items = data.ToList();
            if (storeDelegate != null)
            {
                foreach (var item in items)
                {
                    storeDelegate.Invoke(item);
                }
            }

            await Connector.BulkUpdateAsync(typeof(T), items.Cast<object>(), ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task DeleteAsync(
            IEnumerable<T> data,
            CancellationToken ct = default)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            await Connector.BulkDeleteAsync(typeof(T), data.Cast<object>(), ct).ConfigureAwait(false);
        }

        #endregion
    }
}

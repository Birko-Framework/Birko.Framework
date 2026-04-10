using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>
    /// Native async MSSQL store with bulk operation support via SqlBulkCopy.
    /// Combines single-item and bulk async CRUD operations in one store.
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class AsyncMSSqlStore<T> : AsyncDataBaseBulkStore<MSSqlConnector, T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Initializes a new instance of the AsyncMSSqlStore class.
        /// </summary>
        public AsyncMSSqlStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The remote settings to use.</param>
        public void SetSettings(RemoteSettings settings)
        {
            if (settings != null)
            {
                var pwSettings = new PasswordSettings
                {
                    Location = settings.Location,
                    Name = settings.Name,
                    Password = settings.Password
                };
                base.SetSettings(pwSettings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The password settings to use.</param>
        public override void SetSettings(PasswordSettings settings)
        {
            if (settings is RemoteSettings remote)
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
        protected override async Task CreateCoreAsync(
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
        protected override async Task UpdateCoreAsync(
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
        protected override async Task DeleteCoreAsync(
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

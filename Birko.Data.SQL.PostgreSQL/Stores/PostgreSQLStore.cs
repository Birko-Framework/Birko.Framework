using Birko.Data.SQL.Stores;
using System;
using Birko.Data.Stores;
using Birko.Data.Exceptions;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.PostgreSQL.Stores
{
    /// <summary>
    /// PostgreSQL store with native bulk operation support.
    /// Combines single-item and bulk CRUD operations in one store.
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class PostgreSQLStore<T> : DataBaseBulkStore<SQL.Connectors.PostgreSQLConnector, T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Initializes a new instance of the PostgreSQLStore class.
        /// </summary>
        public PostgreSQLStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The remote settings to use.</param>
        public void SetSettings(Birko.Configuration.RemoteSettings settings)
        {
            if (settings != null)
            {
                base.SetSettings((Birko.Configuration.ISettings)settings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The password settings to use.</param>
        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            if (settings is Birko.Configuration.RemoteSettings remote)
            {
                SetSettings(remote);
            }
            else
            {
                base.SetSettings(settings);
            }
        }

        #region Native Bulk Operations

        /// <inheritdoc />
        protected override void CreateCore(IEnumerable<T> data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            var items = data.ToList();
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                // TASK-556 reversed CR-L190, which made bulk create discard a caller-supplied Guid: it is kept, as on
                // every other path, and a fresh one is assigned only when the Guid is null or Empty.
                item.EnsureGuid();
                item = StoreDelegates.Apply(storeDelegate, item);
                items[itemIndex] = item; // the list is what gets persisted
            }

            // The store-level door has to publish the boundary too, or the connector fix is
            // unreachable through it: these Core overrides bypass the base's per-item write, and the
            // base is the only place that entered the scope. Costs nothing when no context is set.
            using var _tx = EnterTransactionScope();
            Connector.BulkInsert(typeof(T), items.Cast<object>());
        }

        /// <inheritdoc />
        protected override void UpdateCore(IEnumerable<T> data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            var items = data.ToList();
            // TASK-557: the list is what gets persisted, so it takes what the delegate returns.
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                items[itemIndex] = StoreDelegates.Apply(storeDelegate, items[itemIndex]);
            }

            using var _tx = EnterTransactionScope();
            // SH-M306 (TASK-555): rows that matched nothing are reported once the rest are written.
            var unmatched = Connector.BulkUpdate(typeof(T), items.Cast<object>());
            EntityNotFoundException.ThrowIfAny(typeof(T), unmatched.Cast<T>().Select(m => m.Guid ?? Guid.Empty));
        }

        /// <inheritdoc />
        protected override void DeleteCore(IEnumerable<T> data)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            using var _tx = EnterTransactionScope();
            Connector.BulkDelete(typeof(T), data.Cast<object>());
        }

        #endregion
    }
}

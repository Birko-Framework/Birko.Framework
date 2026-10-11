using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using System;
using Birko.Data.Stores;
using Birko.Data.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>
    /// MSSQL store with native bulk operation support via SqlBulkCopy.
    /// Combines single-item and bulk CRUD operations in one store.
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class MSSqlStore<T> : DataBaseBulkStore<MSSqlConnector, T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Initializes a new instance of the MSSqlStore class.
        /// </summary>
        public MSSqlStore()
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
                // Pass the FULL settings through — narrowing to PasswordSettings here dropped
                // UserName/Port/MultipleActiveResultSets/TrustServerCertificate (mirrors AsyncMSSqlStore
                // and the MySQL/PostgreSQL sync stores).
                base.SetSettings((Birko.Configuration.ISettings)settings);
            }
        }

        /// <summary>
        /// Sets the connection settings. Redirects a <see cref="Birko.Configuration.RemoteSettings"/>
        /// arriving through this base overload to the full-settings path.
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
                // TASK-556 (CR-L190 reversed): a caller-supplied Guid is kept, as on every other path.
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

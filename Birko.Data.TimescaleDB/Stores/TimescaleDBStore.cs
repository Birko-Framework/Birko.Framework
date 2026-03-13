using Birko.Data.SQL.Stores;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.TimescaleDB.Stores
{
    /// <summary>
    /// TimescaleDB store with native bulk operation support.
    /// Combines single-item and bulk CRUD operations in one store.
    /// </summary>
    /// <typeparam name="T">The type of entity.</typeparam>
    public class TimescaleDBStore<T> : DataBaseBulkStore<SQL.Connectors.TimescaleDBConnector, T>
        where T : Data.Models.AbstractModel
    {
        /// <summary>
        /// Initializes a new instance of the TimescaleDBStore class.
        /// </summary>
        public TimescaleDBStore()
        {
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The TimescaleDB settings to use.</param>
        public void SetSettings(TimescaleDBSettings settings)
        {
            if (settings != null)
            {
                base.SetSettings((Data.Stores.ISettings)settings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The remote settings to use.</param>
        public void SetSettings(Data.Stores.RemoteSettings settings)
        {
            if (settings is TimescaleDBSettings timescaleSettings)
            {
                SetSettings(timescaleSettings);
            }
            else if (settings != null)
            {
                base.SetSettings((Data.Stores.ISettings)settings);
            }
        }

        /// <summary>
        /// Sets the connection settings.
        /// </summary>
        /// <param name="settings">The password settings to use.</param>
        public override void SetSettings(Data.Stores.PasswordSettings settings)
        {
            if (settings is Data.Stores.RemoteSettings remote)
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
        public override void Create(IEnumerable<T> data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            var items = data.ToList();
            foreach (var item in items)
            {
                item.Guid = Guid.NewGuid();
                storeDelegate?.Invoke(item);
            }

            Connector.BulkInsert(typeof(T), items.Cast<object>());
        }

        /// <inheritdoc />
        public override void Update(IEnumerable<T> data, Data.Stores.StoreDataDelegate<T>? storeDelegate = null)
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

            Connector.BulkUpdate(typeof(T), items.Cast<object>());
        }

        /// <inheritdoc />
        public override void Delete(IEnumerable<T> data)
        {
            if (Connector == null || data == null || !data.Any())
                return;

            Connector.BulkDelete(typeof(T), data.Cast<object>());
        }

        #endregion
    }
}

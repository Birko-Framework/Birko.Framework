using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using System;
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
        public void SetSettings(Data.Stores.RemoteSettings settings)
        {
            if (settings != null)
            {
                var pwSettings = new Data.Stores.PasswordSettings
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

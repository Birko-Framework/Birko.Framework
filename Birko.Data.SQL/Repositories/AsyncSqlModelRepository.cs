using System;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// An async bulk repository over any provider's SQL store (TASK-988), for code that does not name its provider.
    /// <see cref="AsyncDataBaseModelRepository{TConnector, T}"/> needs the connector type, and class generics are
    /// invariant, so it cannot take a store whose connector type the caller does not know.
    /// </summary>
    public class AsyncSqlModelRepository<T> : Data.Repositories.AbstractAsyncBulkRepository<T>
        where T : Data.Models.AbstractModel
    {
        public AsyncSqlModelRepository(IAsyncSqlBulkStore<T> store)
            : base(null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>The store's connector (shared by every store of its database).</summary>
        public AbstractConnector? Connector => (Store as ISqlConnectedStore)?.Connector;
    }
}

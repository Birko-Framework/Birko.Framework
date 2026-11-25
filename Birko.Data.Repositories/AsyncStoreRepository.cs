using System;
using Birko.Data.Models;
using Birko.Data.Stores;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// An async bulk repository over any backend's store (TASK-990), for code that does not name its backend: what
    /// <see cref="StoreFactoryRepositoryExtensions.GetAsyncRepository{T}(IStoreFactory)"/> hands out.
    /// </summary>
    public class AsyncStoreRepository<T> : AbstractAsyncBulkRepository<T>
        where T : AbstractModel
    {
        public AsyncStoreRepository(IAsyncBulkStore<T> store)
            : base(store ?? throw new ArgumentNullException(nameof(store)))
        {
        }
    }

    /// <summary>Repositories from an <see cref="IStoreFactory"/> (TASK-990).</summary>
    public static class StoreFactoryRepositoryExtensions
    {
        /// <summary>
        /// An async bulk repository over <paramref name="factory"/>'s store for <typeparamref name="T"/>. A SQL factory's
        /// own <c>GetAsyncRepository</c> (an <c>AsyncSqlModelRepository</c>, with the connector) wins where the static
        /// type is <c>ISqlStoreFactory</c>.
        /// </summary>
        public static AsyncStoreRepository<T> GetAsyncRepository<T>(this IStoreFactory factory) where T : AbstractModel
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            return new AsyncStoreRepository<T>(factory.GetAsyncStore<T>());
        }
    }
}

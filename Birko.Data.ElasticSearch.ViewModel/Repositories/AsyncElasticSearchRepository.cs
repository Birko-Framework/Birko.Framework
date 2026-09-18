using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nest;

namespace Birko.Data.ElasticSearch.Repositories
{
    /// <summary>
    /// Async ElasticSearch repository with bulk operations support.
    /// Inherits from AbstractAsyncBulkViewModelRepository to provide bulk operations via ElasticSearch's async _bulk API.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AsyncElasticSearchRepository<TViewModel, TModel> : Data.Repositories.AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Data.Models.AbstractModel
        where TViewModel : Data.Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// Gets the ElasticSearch store with bulk operations support.
        /// This works with wrapped stores (e.g., tenant wrappers).
        /// </summary>
        public Stores.AsyncElasticSearchStore<TModel>? ElasticSearchStore => Store?.GetUnwrappedStore<TModel, Stores.AsyncElasticSearchStore<TModel>>();

        #endregion

        #region Constructors and Initialization
        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async ElasticSearch store to use. Can be wrapped (e.g., by tenant wrappers).</param>

        public AsyncElasticSearchRepository(IAsyncStore<TModel>? store)
            : base(null)
        {
            if (store != null && !store.IsStoreOfType<TModel, AsyncElasticSearchStore<TModel>>())
            {
                throw new ArgumentException(
                    "Store must be of type AsyncElasticSearchStore<TModel> or a wrapper around it (e.g., AsyncTenantStoreWrapper).",
                    nameof(store));
            }
            // Set the store after validation - base constructor handles null by creating default
            if (store != null)
            {
                Store = store;
            }
        }

        #endregion

        #region Query and Count Operations

        /// <summary>
        /// Asynchronously counts documents matching the specified query container.
        /// Mirrors the sync repository's <c>Count(QueryContainer)</c> (CR-L115).
        /// </summary>
        /// <param name="query">The query container to match.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The count of matching documents, or 0 when the store is unavailable.</returns>
        public virtual async Task<long> CountAsync(QueryContainer? query, CancellationToken ct = default)
        {
            var store = ElasticSearchStore;
            if (store == null)
            {
                return 0;
            }

            return await store.CountAsync(query, ct).ConfigureAwait(false);
        }

        #endregion

        #region Index Management

        /// <summary>
        /// Asynchronously clears the cache for the ElasticSearch index.
        /// Mirrors the sync repository's <c>ClearCache()</c> (CR-L115).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public virtual async Task ClearCacheAsync(CancellationToken ct = default)
        {
            var store = ElasticSearchStore;
            if (store == null)
            {
                return;
            }

            await store.ClearCacheAsync(ct).ConfigureAwait(false);
        }

        #endregion

        #region Advanced Read Operations

        /// <summary>
        /// Reads documents using a custom search request, streaming the results.
        /// Mirrors the sync repository's <c>Read(SearchRequest)</c> (CR-L115).
        /// </summary>
        /// <param name="request">The search request.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An async stream of matching view models.</returns>
        public virtual async IAsyncEnumerable<TViewModel> ReadAsync(
            SearchRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var store = ElasticSearchStore;
            if (store == null)
            {
                yield break;
            }

            await foreach (var item in store.ReadStreamAsync(request, ct).ConfigureAwait(false))
            {
                var instance = LoadInstance(item);
                if (instance != null)
                {
                    yield return instance;
                }
            }
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Abstract async bulk repository with ViewModel/Model separation and change tracking.
    /// Extends <see cref="AbstractAsyncViewModelRepository{TViewModel, TModel}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AbstractAsyncBulkViewModelRepository<TViewModel, TModel>
        : AbstractAsyncViewModelRepository<TViewModel, TModel>, IAsyncBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties

        /// <summary>
        /// SH-M209 (TASK-162): a store that is not a bulk store made every bulk call yield nothing or do nothing, where
        /// the sync twin throws. Now this one throws too.
        /// </summary>
        private InvalidOperationException NotBulk()
            => Store == null ? NoStore() : new($"Store is not type of {typeof(Stores.IAsyncBulkStore<TModel>)}");

        /// <summary>SH-M214 (TASK-162): a paged read is ordered by Guid, so its window is stable.</summary>
        protected static Stores.OrderBy<TModel>? PageOrder(int? limit, int? offset)
            => limit != null || offset != null ? Stores.OrderBy<TModel>.By(x => x.Guid) : null;

        /// <summary>
        /// Gets the async bulk store for bulk operations.
        /// </summary>
        protected Stores.IAsyncBulkStore<TModel>? BulkStore
        {
            get
            {
                return Store as Stores.IAsyncBulkStore<TModel>;
            }
        }

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async store implementing both IAsyncStore and IAsyncBulkStore.</param>
        public AbstractAsyncBulkViewModelRepository(
            Stores.IAsyncStore<TModel>? store = null)
            : base(store)
        {
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <summary>
        /// Asynchronously creates multiple entities.
        /// </summary>
        public virtual async Task CreateAsync(IEnumerable<TViewModel> data, Stores.StoreDataDelegate<TModel>? storeDelegate = null, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (BulkStore == null) throw NotBulk(); // SH-M209 (TASK-162)
            if (data == null) return;

            // CR-M179: route through LoadModelInstance so the ViewModel→Model mapping matches the
            // single-item path (uniform behavior). CR-L237: LoadModelInstance returns a non-null TModel
            // (CreateModelInstance → Store.CreateInstance() / Activator.CreateInstance<TModel>()), so the
            // old .Where(m => m != null).ToList()! was dead filtering + a needless snapshot allocation —
            // the store enumerates the projection lazily (matches the sync bulk sibling).
            await BulkStore.CreateAsync(data.Select(LoadModelInstance), storeDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously reads entities with optional filtering, limit, and offset.
        /// </summary>
        public virtual async IAsyncEnumerable<TViewModel> ReadAsync(
            Expression<Func<TModel, bool>>? filter = null,
            int? limit = null,
            int? offset = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (BulkStore is null) throw NotBulk(); // SH-M209 (TASK-162)

            var models = await BulkStore.ReadAsync(filter, PageOrder(limit, offset), limit, offset, ct); // SH-M214
            foreach (var model in models)
            {
                // CR-M179: use LoadInstance so change-tracking (StoreHash) is populated for bulk-read
                // entities — otherwise a later single-item UpdateAsync sees no stored hash and the
                // optimistic no-op-update skip is defeated.
                var viewModel = LoadInstance(model);
                if (viewModel != null)
                {
                    yield return viewModel;
                }
            }
        }

        /// <summary>
        /// Asynchronously updates multiple entities.
        /// </summary>
        public virtual async Task UpdateAsync(IEnumerable<TViewModel> data, Stores.StoreDataDelegate<TModel>? storeDelegate = null, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (BulkStore == null) throw NotBulk(); // SH-M209 (TASK-162)
            if (data == null) return;

            // CR-M179 mapping. SH-H034: map onto the STORED row, not onto a fresh instance, or every
            // column this ViewModel does not map is blanked. Materialized deliberately (reversing
            // CR-L237's lazy projection here) so every read happens before any write, and so the
            // N-reads cost is visible rather than hidden in a Select.
            var items = new List<TModel>();
            foreach (var viewModel in data)
            {
                // Only the merged model is needed: the bulk path has never run the no-op hash check.
                items.Add((await LoadModelInstanceForUpdateAsync(viewModel, ct).ConfigureAwait(false)).Item);
            }
            await BulkStore.UpdateAsync(items, storeDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action.
        /// </summary>
        public virtual async Task UpdateAsync(Expression<Func<TModel, bool>> filter, Action<TModel> updateAction, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (BulkStore == null) throw NotBulk(); // SH-M209 (TASK-162)
            await BulkStore.UpdateAsync(filter, updateAction, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously updates specific properties on all entities matching the filter.
        /// </summary>
        public virtual async Task UpdateAsync(Expression<Func<TModel, bool>> filter, Stores.PropertyUpdate<TModel> updates, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (BulkStore == null) throw NotBulk(); // SH-M209 (TASK-162)
            await BulkStore.UpdateAsync(filter, updates, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously deletes all entities matching the specified filter.
        /// </summary>
        public virtual async Task DeleteAsync(Expression<Func<TModel, bool>> filter, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (BulkStore == null) throw NotBulk(); // SH-M209 (TASK-162)
            await BulkStore.DeleteAsync(filter, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously deletes multiple entities.
        /// </summary>
        public virtual async Task DeleteAsync(IEnumerable<TViewModel> data, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (BulkStore == null) throw NotBulk(); // SH-M209 (TASK-162)
            if (data == null) return;

            // CR-M179 mapping; CR-L237 drops the dead .Where(m => m != null).ToList()! (lazy projection).
            var models = data.Select(LoadModelInstance).ToList();
            await BulkStore.DeleteAsync(models, ct).ConfigureAwait(false);
            foreach (var model in models) RemoveHash(model); // SH-M207 (TASK-162)
        }

        #endregion

        // No DestroyAsync override: BulkStore is `Store as IAsyncBulkStore<TModel>` — the SAME
        // instance the base AbstractAsyncViewModelRepository.DestroyAsync already destroys.
        // Overriding to also call BulkStore.DestroyAsync destroyed one store twice, which is unsafe
        // for a non-idempotent DestroyAsync (double-dispose / double-close) — the ViewModel analogue
        // of CR-H080, surfaced by the CR-L234 review.
    }
}

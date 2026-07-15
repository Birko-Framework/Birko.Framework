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
            if (ReadMode) throw new AccessViolationException("Repository is in Read Mode"); // CR-M180
            if (BulkStore == null || data == null)
            {
                return;
            }

            // CR-M179: route through LoadModelInstance so the ViewModel→Model mapping matches the
            // single-item path (uniform behavior).
            var models = data.Select(LoadModelInstance).Where(m => m != null).ToList()!;

            await BulkStore.CreateAsync(models, storeDelegate, ct).ConfigureAwait(false);
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
            if (BulkStore is null)
            {
                yield break;
            }

            var models = await BulkStore.ReadAsync(filter, null, limit, offset, ct);
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
            if (ReadMode) throw new AccessViolationException("Repository is in Read Mode"); // CR-M180
            if (BulkStore == null || data == null)
            {
                return;
            }

            var models = data.Select(LoadModelInstance).Where(m => m != null).ToList()!; // CR-M179

            await BulkStore.UpdateAsync(models, storeDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously updates all entities matching the filter by applying the specified action.
        /// </summary>
        public virtual async Task UpdateAsync(Expression<Func<TModel, bool>> filter, Action<TModel> updateAction, CancellationToken ct = default)
        {
            if (ReadMode) throw new AccessViolationException("Repository is in Read Mode"); // CR-M180
            if (BulkStore == null) return;
            await BulkStore.UpdateAsync(filter, updateAction, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously updates specific properties on all entities matching the filter.
        /// </summary>
        public virtual async Task UpdateAsync(Expression<Func<TModel, bool>> filter, Stores.PropertyUpdate<TModel> updates, CancellationToken ct = default)
        {
            if (ReadMode) throw new AccessViolationException("Repository is in Read Mode"); // CR-M180
            if (BulkStore == null) return;
            await BulkStore.UpdateAsync(filter, updates, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously deletes all entities matching the specified filter.
        /// </summary>
        public virtual async Task DeleteAsync(Expression<Func<TModel, bool>> filter, CancellationToken ct = default)
        {
            if (ReadMode) throw new AccessViolationException("Repository is in Read Mode"); // CR-M180
            if (BulkStore == null) return;
            await BulkStore.DeleteAsync(filter, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously deletes multiple entities.
        /// </summary>
        public virtual async Task DeleteAsync(IEnumerable<TViewModel> data, CancellationToken ct = default)
        {
            if (ReadMode) throw new AccessViolationException("Repository is in Read Mode"); // CR-M180
            if (BulkStore == null || data == null)
            {
                return;
            }

            var models = data.Select(LoadModelInstance).Where(m => m != null).ToList()!; // CR-M179

            await BulkStore.DeleteAsync(models, ct).ConfigureAwait(false);
        }

        #endregion

        // No DestroyAsync override: BulkStore is `Store as IAsyncBulkStore<TModel>` — the SAME
        // instance the base AbstractAsyncViewModelRepository.DestroyAsync already destroys.
        // Overriding to also call BulkStore.DestroyAsync destroyed one store twice, which is unsafe
        // for a non-idempotent DestroyAsync (double-dispose / double-close) — the ViewModel analogue
        // of CR-H080, surfaced by the CR-L234 review.
    }
}

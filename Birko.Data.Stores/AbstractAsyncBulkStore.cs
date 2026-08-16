using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Provides a base implementation for async bulk data stores.
    /// Extends <see cref="AbstractAsyncStore{T}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncBulkStore<T> : AbstractAsyncStore<T>, IAsyncBulkStore<T>
        where T : Models.AbstractModel
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncBulkStore class.
        /// </summary>
        public AbstractAsyncBulkStore()
        {
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        public virtual async Task CreateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await CreateCoreAsync(data, storeDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core bulk create implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task CreateCoreAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task<IEnumerable<T>> ReadAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await ReadCoreAsync(filter, orderBy, limit, offset, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core bulk read implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<IEnumerable<T>> ReadCoreAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        {
            return await ReadAsync(null, null, null, null, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public virtual Task<T?> ReadFirstAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            // base = AbstractAsyncStore<T> — resolves to the single-result ReadAsync(filter, ct) the bulk overload hides here.
            return base.ReadAsync(filter, ct);
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await UpdateCoreAsync(data, storeDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core bulk update implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task UpdateCoreAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual Task UpdateAsync(
            Expression<Func<T, bool>> filter,
            PropertyUpdate<T> updates,
            CancellationToken ct = default)
        {
            RequireFilter(filter, "update");
            RequireBoundedFilter(filter, "update");
            return UpdateAsync(filter, entity => updates.ApplyTo(entity), ct);
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(
            Expression<Func<T, bool>> filter,
            Action<T> updateAction,
            CancellationToken ct = default)
        {
            RequireFilter(filter, "update");
            RequireBoundedFilter(filter, "update");
            var items = (await ReadAsync(filter, null, null, null, ct).ConfigureAwait(false)).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                await UpdateAsync(item, ct: ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public virtual async Task DeleteAsync(
            IEnumerable<T> data,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await DeleteCoreAsync(data, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core bulk delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task DeleteCoreAsync(
            IEnumerable<T> data,
            CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task DeleteAsync(
            Expression<Func<T, bool>> filter,
            CancellationToken ct = default)
        {
            RequireFilter(filter, "delete");
            RequireBoundedFilter(filter, "delete");
            var items = (await ReadAsync(filter, null, null, null, ct).ConfigureAwait(false)).ToList();
            await DeleteAsync(items, ct).ConfigureAwait(false);
        }

        /// <inheritdoc cref="AbstractBulkStore{T}.UpdateAll(PropertyUpdate{T})"/>
        public virtual Task UpdateAllAsync(PropertyUpdate<T> updates, CancellationToken ct = default)
        {
            return UpdateAllAsync(entity => updates.ApplyTo(entity), ct);
        }

        /// <inheritdoc cref="AbstractBulkStore{T}.UpdateAll(PropertyUpdate{T})"/>
        public virtual async Task UpdateAllAsync(Action<T> updateAction, CancellationToken ct = default)
        {
            var items = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                await UpdateAsync(item, ct: ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc cref="AbstractBulkStore{T}.DeleteAll()"/>
        public virtual async Task DeleteAllAsync(CancellationToken ct = default)
        {
            var items = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            await DeleteAsync(items, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Guards the filter-based destructive overloads.
        /// </summary>
        /// <remarks>
        /// <para><b>SH-M023.</b> These overloads declare <c>filter</c> non-nullable and then never checked it.
        /// They are read-then-loop — <c>Delete(null!)</c> called <c>Read(null, ...)</c>, where a null filter
        /// legitimately means <i>read everything</i>, and deleted the entire result. Because every statement
        /// they issue is per-row and therefore carries its own key, no backend query guard can see this: the
        /// damage is "affected every row", never "a statement with no predicate". So it has to be refused
        /// here, at the boundary where the intent is still visible.</para>
        /// <para>Deliberately an <see cref="ArgumentNullException"/>: the caller passed null for a parameter
        /// declared non-nullable. Wanting every row is expressed by the <c>*All</c> methods instead.</para>
        /// </remarks>
        protected static void RequireFilter(Expression<Func<T, bool>> filter, string operation)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(
                    nameof(filter),
                    $"A filter is required to {operation} {typeof(T).Name}: a missing filter would affect every "
                        + $"row. To target every row deliberately use {(operation == "delete" ? "DeleteAllAsync()" : "UpdateAllAsync(updates)")}.");
            }
        }

        /// <inheritdoc cref="AbstractBulkStore{T}.RequireBoundedFilter(Expression{Func{T, bool}}, string)"/>
        protected static void RequireBoundedFilter(Expression<Func<T, bool>>? filter, string operation)
        {
            if (filter == null) return;                   // RequireFilter owns the null case.
            if (Data.Expressions.PredicateScope.IsExplicitAllRows(filter)) return;
            if (!Data.Expressions.PredicateScope.ReducesToAllRows(filter)) return;

            // TASK-215 / § SH-H037: name the door this caller actually HAS. An async store has no
            // DeleteAll() — pointing at it would be a refusal whose opt-out does not compile.
            throw new Data.Exceptions.WholeTableWriteException(
                operation, typeof(T).Name, "every stored entity of that type",
                operation == "delete" ? "DeleteAllAsync()" : "UpdateAllAsync(updates)");
        }


        #endregion
    }
}

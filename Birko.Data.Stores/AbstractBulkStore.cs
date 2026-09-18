using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Abstract base class for synchronous bulk data stores.
    /// Extends <see cref="AbstractStore{T}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractBulkStore<T> : AbstractStore<T>, IBulkStore<T>
         where T : Models.AbstractModel
    {
        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        public virtual void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            CreateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core bulk create implementation. Override in concrete stores.
        /// </summary>
        protected abstract void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            EnsureInitialized();
            return ReadCore(filter, orderBy, limit, offset);
        }

        /// <summary>
        /// Core bulk read implementation. Override in concrete stores.
        /// </summary>
        protected abstract IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null);

        /// <inheritdoc />
        public virtual IEnumerable<T> Read()
        {
            return Read(null, null, null, null);
        }

        /// <inheritdoc />
        public virtual T? ReadFirst(Expression<Func<T, bool>>? filter = null)
        {
            // base = AbstractStore<T> — resolves to the single-result Read(filter) the bulk overload hides here.
            return base.Read(filter);
        }

        /// <inheritdoc />
        public virtual void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            UpdateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core bulk update implementation. Override in concrete stores.
        /// </summary>
        protected abstract void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
        {
            RequireFilter(filter, "update");
            RequireBoundedFilter(filter, "update");
            Update(filter, entity => updates.ApplyTo(entity));
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        {
            RequireFilter(filter, "update");
            RequireBoundedFilter(filter, "update");
            var items = Read(filter, null, null, null).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                Update(item);
            }
        }

        /// <summary>
        /// Updates EVERY row — the explicit all-rows door (SH-M023). Equivalent to
        /// <c>Update(x =&gt; true, updates)</c>, and the recommended spelling: a reader of the call site does
        /// not have to notice that the predicate is a constant.
        /// </summary>
        public virtual void UpdateAll(PropertyUpdate<T> updates)
        {
            UpdateAll(entity => updates.ApplyTo(entity));
        }

        /// <inheritdoc cref="UpdateAll(PropertyUpdate{T})"/>
        public virtual void UpdateAll(Action<T> updateAction)
        {
            var items = Read().ToList();
            foreach (var item in items)
            {
                updateAction(item);
                Update(item);
            }
        }

        /// <inheritdoc />
        public virtual void Delete(IEnumerable<T> data)
        {
            EnsureInitialized();
            DeleteCore(data);
        }

        /// <summary>
        /// Core bulk delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract void DeleteCore(IEnumerable<T> data);

        /// <inheritdoc />
        public virtual void Delete(Expression<Func<T, bool>> filter)
        {
            RequireFilter(filter, "delete");
            RequireBoundedFilter(filter, "delete");
            var items = Read(filter, null, null, null).ToList();
            Delete(items);
        }

        /// <summary>
        /// Deletes EVERY row — the explicit all-rows door (SH-M023). Equivalent to
        /// <c>Delete(x =&gt; true)</c>. Use <c>Destroy()</c> to discard the store instead of emptying it.
        /// </summary>
        public virtual void DeleteAll()
        {
            Delete(Read().ToList());
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
                        + $"row. To target every row deliberately use {(operation == "delete" ? "DeleteAll()" : "UpdateAll(updates)")}.");
            }
        }

        /// <summary>
        /// Refuses a filter-based destructive operation whose predicate constrains **nothing**, unless every
        /// row was asked for explicitly. The scope companion to <see cref="RequireFilter"/>: that one catches
        /// a filter that is missing, this one a filter that is present and covers everything anyway.
        /// </summary>
        /// <remarks>
        /// <para><b>TASK-212.</b> For a backend that hands the raw <see cref="Expression"/> to a driver, a
        /// guard on the emitted query cannot see this. Measured on MongoDB.Driver 3.2.0,
        /// <c>x =&gt; !empty.Contains(x.Field)</c> renders <c>{ "Field": { "$nin": [] } }</c> — a one-element
        /// document indistinguishable by inspection from an ordinary predicate, while matching every document.
        /// That is the same shape as the SQL side's <c>1 = 1</c>, which satisfied a guard testing whether
        /// anything had been rendered (TASK-137). Both say the same thing: <b>test what the operation means,
        /// not whether output was produced.</b></para>
        /// <para>The refusal is <see cref="Data.Exceptions.WholeTableWriteException"/> — the same type the SQL
        /// connectors throw — so one <c>catch</c> selects the refusal on every backend. It names the
        /// deliberate door, which exists on this very class and is inherited by every store that reaches
        /// here.</para>
        /// <para><b>An explicit <c>x =&gt; true</c> is NOT refused.</b> It is the documented synonym for
        /// <c>DeleteAll()</c> / <c>UpdateAll()</c>, checked first so the guard has a door rather than being a
        /// wall (§ SH-H037). Only a predicate that *happens* to cover everything is refused.</para>
        /// <para>Call it from any override that bypasses this class's own wrappers — the sweep behind SH-M023
        /// found ten such overrides across three backends, so the guard has to be reachable, not implicit.</para>
        /// <para><b>TASK-329 — the rule now lives in one place, not here.</b> This method and its twin on
        /// <c>AbstractAsyncBulkStore</c> were two implementations of one rule, and the SQL bulk stores derive
        /// from neither hierarchy: they implement <c>IBulkStore&lt;T&gt;</c> / <c>IAsyncBulkStore&lt;T&gt;</c>
        /// directly, so they inherited no guard and rewrote whole tables silently (measured: 3 of 3 rows,
        /// <c>thrown=NONE</c>). Both now delegate to <see cref="Data.Expressions.BoundedFilterGuard"/> and
        /// pass only the door name, which is the sole thing that genuinely differs per caller. <b>Do not
        /// re-inline the checks here</b> — a fourth copy is how the third one came to be missed.</para>
        /// </remarks>
        protected static void RequireBoundedFilter(Expression<Func<T, bool>>? filter, string operation)
        {
            // TASK-329: the rule itself lives in ONE place now. This used to be an inline copy, and
            // its twin in the other hierarchy was a second — which is how the SQL bulk stores, which
            // derive from neither, ended up with none at all and rewrote whole tables silently.
            // Only the door name differs per caller, so only the door name is passed.
            Data.Expressions.BoundedFilterGuard.Require(
                filter, operation, typeof(T).Name,
                operation == "delete" ? "DeleteAll()" : "UpdateAll(updates)");
        }


        #endregion
    }
}

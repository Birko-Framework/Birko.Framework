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
            Update(filter, entity => updates.ApplyTo(entity));
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        {
            RequireFilter(filter, "update");
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


        #endregion
    }
}

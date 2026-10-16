using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using Birko.Data.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Bulk database store that provides optimized bulk operations.
    /// Extends <see cref="DataBaseStore{DB, T}"/> with bulk CRUD capabilities.
    /// Uses Template Method pattern — concrete stores override *Core methods.
    /// </summary>
    /// <typeparam name="DB">The type of database connector, must inherit from <see cref="AbstractConnector"/>.</typeparam>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class DataBaseBulkStore<DB, T> : DataBaseStore<DB, T>, IBulkStore<T>, IAggregatableStore<T>
        where T : Models.AbstractModel
        where DB : AbstractConnector
    {
        /// <summary>
        /// Initializes a new instance of the DataBaseBulkStore class.
        /// </summary>
        public DataBaseBulkStore()
            : base()
        {
        }

        #region Bulk Read Operations

        /// <inheritdoc />
        public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            EnsureInitialized();
            return ReadCore(filter, orderBy, limit, offset);
        }

        /// <summary>
        /// Core bulk read implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual IEnumerable<T> ReadCore(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
        {
            using var _tx = EnterTransactionScope();
            // SH-M025 (TASK-170): materialised. The connector's Select is an iterator that holds its connection and
            // reader open until enumeration ends, so handing it out leaked a pooled connection on an abandoned
            // enumerator, and ran any write inside `foreach (var x in store.Read(f))` against a live reader.
            return Connector?.Select(typeof(T), filter as LambdaExpression, orderBy?.ToDictionary(), limit, offset)?.OfType<T>().ToList()
                ?? (IEnumerable<T>)Enumerable.Empty<T>();
        }

        /// <inheritdoc />
        public virtual IEnumerable<T> Read()
        {
            return Read(null, null, null, null);
        }

        /// <inheritdoc />
        public virtual T? ReadFirst(Expression<Func<T, bool>>? filter = null)
        {
            // base = DataBaseStore<DB, T> — resolves to the single-result Read(filter) (LIMIT 1 via ReadCore)
            // the bulk overload hides here.
            return base.Read(filter);
        }

        #endregion

        #region Bulk Write Operations

        /// <inheritdoc />
        public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            try
            {
                Atomically(() => CreateCore(data, storeDelegate));
            }
            catch (Exception ex) when (DuplicateCreate.IsDuplicate(Connector, ex))
            {
                // TASK-567. Here, around the seam, so a provider's native bulk insert (COPY, multi-row INSERT) is covered
                // whether or not it overrides CreateCore (rule 12).
                throw DuplicateCreate.Refusal(typeof(T), ex);
            }
        }

        /// <summary>
        /// SH-M028 (TASK-629). Runs one bulk collection write as ONE transaction. Each item used to commit on its own, so a
        /// failure (or a cancelled token) at item k left items 0..k-1 written and the caller unable to tell how far it
        /// got. Inside an existing boundary (an ambient transaction for this database, or the store's own transaction
        /// context) nothing changes: the write joins it, as every write does (TASK-240). Otherwise the batch opens its
        /// own transaction and publishes it as the ambient scope, so each per-item write and a provider's native bulk
        /// insert join it; it commits at the end and rolls back on any failure.
        /// </summary>
        /// <remarks>
        /// One exception commits: <see cref="EntityNotFoundException"/> from a collection update is TASK-555's report of
        /// the items that were not stored, raised AFTER every stored item was written, which is that contract. It is a
        /// zero-row UPDATE, not a database error, so the transaction is still usable.
        /// </remarks>
        protected void Atomically(Action write)
        {
            if (Connector == null || TransactionContext != null || AmbientSqlTransaction.Find(Connector.Settings.GetId()) != null)
            {
                using var joined = EnterTransactionScope();
                write();
                return;
            }

            using var connection = Connector.CreateConnection(Connector.Settings);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var scope = AmbientSqlTransaction.Enter(Connector.Settings.GetId(), connection, transaction);
            try
            {
                write();
            }
            catch (EntityNotFoundException)
            {
                scope.Dispose();
                transaction.Commit();
                throw;
            }
            catch
            {
                scope.Dispose();
                transaction.Rollback();
                throw;
            }
            scope.Dispose();
            transaction.Commit();
        }

        /// <summary>
        /// Core bulk create implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual void CreateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            foreach (var item in data)
            {
                Create(item, storeDelegate);
            }
        }

        /// <inheritdoc />
        public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            try
            {
                Atomically(() => UpdateCore(data, storeDelegate));
            }
            catch (Exception ex) when (ex is not Birko.Data.Exceptions.StoreUniqueViolationException && IsDuplicateKeyException(ex))
            {
                // TASK-632: an update colliding with a unique index is recognisable on every provider, as a create is.
                throw Birko.Data.Exceptions.StoreException.UniqueViolation(typeof(T), ex);
            }
        }

        /// <summary>
        /// Core bulk update implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual void UpdateCore(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
        {
            // TASK-555: each item's miss is collected rather than ending the batch — the stored ones are still written.
            var missing = new List<Guid>();
            foreach (var item in data)
            {
                try
                {
                    Update(item, storeDelegate);
                }
                catch (EntityNotFoundException ex)
                {
                    missing.AddRange(ex.Guids);
                }
            }
            EntityNotFoundException.ThrowIfAny(typeof(T), missing);
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, Action<T> updateAction)
        {
            // SH-M023: read-then-loop, so no conditionless SQL is emitted and the connector guard cannot see
            // this path — but a null filter still means Read(null) = every row, then mutate every one.
            RequireFilter(filter, "update");
            // TASK-329: that comment reasoned about NULL and stopped there. A filter that is present but
            // reduces to every row got through, and this path emits no conditionless statement for the
            // connector to refuse. Measured before the fix: 3 of 3 rows rewritten, thrown=NONE.
            RequireBoundedFilter(filter, "update");
            var items = Read(filter, null, null, null).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                // SH-M434 (TASK-170): a row deleted after the read no longer matches the filter, so it is skipped, as a
                // SQL UPDATE … WHERE skips it. Throwing left the earlier rows written and the later ones not.
                try { Update(item); }
                catch (Exceptions.EntityNotFoundException) { }
            }
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates)
        {
            RequireFilter(filter, "update");
            UpdateInternal(filter, updates, SQL.DataBase.IsExplicitAllRows(filter as LambdaExpression));
        }

        /// <summary>
        /// Updates EVERY row with <paramref name="updates"/> — the explicit all-rows door (SH-H002).
        /// Equivalent to <c>Update(x =&gt; true, updates)</c>, and the recommended spelling.
        /// </summary>
        public virtual void UpdateAll(PropertyUpdate<T> updates)
        {
            UpdateInternal(null, updates, allowAllRows: true);
        }

        /// <summary>
        /// Deletes EVERY row — the explicit all-rows door (SH-H002). Equivalent to
        /// <c>Delete(x =&gt; true)</c>. Use <c>Destroy()</c> to drop the table instead of emptying it.
        /// </summary>
        public virtual void DeleteAll()
        {
            EnsureInitialized();
            using var _tx = EnterTransactionScope();
            Connector?.DeleteAll(typeof(T));
        }

        /// <summary>
        /// TASK-329 — refuses a filter that <b>reduces</b> to every row on a destructive write. See the
        /// async twin; the rule lives once in <c>BoundedFilterGuard</c> and only the door name differs.
        /// </summary>
        private static void RequireBoundedFilter(Expression<Func<T, bool>>? filter, string operation)
        {
            Data.Expressions.BoundedFilterGuard.Require(
                filter, operation, typeof(T).Name,
                operation == "delete" ? "DeleteAll()" : "UpdateAll(updates)");
        }

        /// <summary>
        /// Guards the filter-based destructive overloads. A null filter is refused rather than treated as
        /// "every row" — the parameter is already declared non-nullable, so this closes the gap between the
        /// declaration and the behaviour (SH-M023).
        /// </summary>
        private static void RequireFilter(Expression<Func<T, bool>> filter, string operation)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(
                    nameof(filter),
                    $"A filter is required to {operation} {typeof(T).Name}: a missing filter would affect every "
                        + $"row. To target every row deliberately use {(operation == "delete" ? "DeleteAll()" : "UpdateAll(updates)")} "
                        + "or an explicit `x => true` filter.");
            }
        }

        /// <summary>
        /// Shared body of the filter overload and <see cref="UpdateAll"/>. A null filter is only legal with
        /// <paramref name="allowAllRows"/>; the public overload guards it first.
        /// </summary>
        private void UpdateInternal(Expression<Func<T, bool>>? filter, PropertyUpdate<T> updates, bool allowAllRows)
        {
            EnsureInitialized();
            if (Connector == null || updates.Assignments.Count == 0) return;
            using var _tx = EnterTransactionScope();

            var table = SQL.DataBase.LoadTable(typeof(T));
            var (fields, values) = PropertyUpdateSqlTranslator.Translate(updates, Connector);
            var conditions = SQL.DataBase.ParseConditionExpression(filter as LambdaExpression);
            Connector.Update(table.Name, fields, values, conditions, true, allowAllRows);
        }

        /// <inheritdoc />
        public void Delete(IEnumerable<T> data)
        {
            EnsureInitialized();
            Atomically(() => DeleteCore(data));
        }

        /// <summary>
        /// Core bulk delete implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual void DeleteCore(IEnumerable<T> data)
        {
            foreach (var item in data)
            {
                Delete(item);
            }
        }

        /// <inheritdoc />
        public virtual void Delete(Expression<Func<T, bool>> filter)
        {
            RequireFilter(filter, "delete");
            EnsureInitialized();
            if (Connector == null) return;
            using var _tx = EnterTransactionScope();

            if (SQL.DataBase.IsExplicitAllRows(filter as LambdaExpression))
            {
                Connector.DeleteAll(typeof(T));
                return;
            }
            Connector.Delete(typeof(T), filter as LambdaExpression);
        }

        #endregion

        #region Aggregation

        /// <summary>
        /// Executes a synchronous aggregation query using SQL GROUP BY.
        /// </summary>
        public IReadOnlyList<AggregateResult> Aggregate(AggregateQuery<T> query)
        {
            EnsureInitialized();
            if (Connector == null) return Array.Empty<AggregateResult>();
            using var _tx = EnterTransactionScope();


            var results = new List<AggregateResult>();
            foreach (var row in Connector.SelectAggregate(typeof(T), query))
            {
                results.Add(row);
            }
            return results.AsReadOnly();
        }

        #endregion
    }
}

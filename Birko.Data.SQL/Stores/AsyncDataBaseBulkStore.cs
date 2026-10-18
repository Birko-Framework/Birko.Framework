using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using Birko.Data.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Async bulk database store that provides optimized bulk operations.
    /// Extends <see cref="AsyncDataBaseStore{DB, T}"/> with async bulk CRUD capabilities.
    /// Uses Template Method pattern — concrete stores override *CoreAsync methods.
    /// </summary>
    /// <typeparam name="DB">The type of database connector, must inherit from <see cref="AbstractConnector"/>.</typeparam>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncDataBaseBulkStore<DB, T> : AsyncDataBaseStore<DB, T>, IAsyncBulkStore<T>, IAsyncAggregatableStore<T>
        where T : Models.AbstractModel
        where DB : AbstractConnector
    {
        /// <summary>
        /// Initializes a new instance of the AsyncDataBaseBulkStore class.
        /// </summary>
        public AsyncDataBaseBulkStore()
            : base()
        {
        }

        #region Bulk Read Operations

        /// <inheritdoc />
        public async Task<IEnumerable<T>> ReadAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await ReadCoreAsync(filter, orderBy, limit, offset, ct);
        }

        /// <summary>
        /// Core bulk read implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual async Task<IEnumerable<T>> ReadCoreAsync(
            Expression<Func<T, bool>>? filter = null,
            OrderBy<T>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            if (Connector == null) return Enumerable.Empty<T>();
            using var _tx = EnterTransactionScope();

            if (AsyncConnector != null)
            {
                var items = new List<T>();
                await foreach (var item in AsyncConnector.SelectAsync(typeof(T), filter as LambdaExpression, orderBy?.ToDictionary(), limit, offset, ct))
                {
                    if (item is T typed) items.Add(typed);
                }
                return items;
            }

            // SH-M024 (TASK-170): Select is an iterator, so Task.Run only built it and the query ran later, on the caller's
            // thread, outside its try/catch. It is enumerated inside the task now.
            var results = await Task.Run(() => Connector!.Select(typeof(T), filter as LambdaExpression, orderBy?.ToDictionary(), limit, offset)?.OfType<T>().ToList(), ct);
            return results ?? (IEnumerable<T>)Enumerable.Empty<T>();
        }

        /// <inheritdoc />
        public virtual async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default)
        {
            return await ReadAsync(null, null, null, null, ct);
        }

        /// <inheritdoc />
        public virtual Task<T?> ReadFirstAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            // base = AsyncDataBaseStore<DB, T> — resolves to the single-result ReadAsync(filter, ct)
            // (LIMIT 1 via ReadCoreAsync) the bulk overload hides here.
            return base.ReadAsync(filter, ct);
        }

        #endregion

        #region Bulk Write Operations

        /// <summary>
        /// SH-M028 (TASK-629). Runs one async bulk collection write as ONE transaction. Each item used to commit on its own, so a
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
        protected async Task AtomicallyAsync(Func<Task> write, CancellationToken ct)
        {
            if (Connector == null || TransactionContext != null || AmbientSqlTransaction.Find(Connector.Settings.GetId()) != null)
            {
                using var joined = EnterTransactionScope();
                await write().ConfigureAwait(false);
                return;
            }

            await using var connection = Connector.CreateConnection(Connector.Settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            var scope = AmbientSqlTransaction.Enter(Connector.Settings.GetId(), connection, transaction);
            try
            {
                await write().ConfigureAwait(false);
            }
            catch (Exceptions.EntityNotFoundException)
            {
                scope.Dispose();
                await CommitAndRunCallbacksAsync(transaction, ct).ConfigureAwait(false);
                throw;
            }
            catch
            {
                scope.Dispose();
                AmbientSqlTransaction.DiscardCommitted(transaction);
                // Not the caller's token: a cancelled batch must still be rolled back.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            scope.Dispose();
            await CommitAndRunCallbacksAsync(transaction, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Commits a batch's own transaction, then runs the work registered to follow it (TASK-669). A failed commit
        /// discards that work; a callback's failure is recorded on the connector (channel <c>AfterCommit</c>).
        /// </summary>
        private async Task CommitAndRunCallbacksAsync(System.Data.Common.DbTransaction transaction, CancellationToken ct)
        {
            try
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                AmbientSqlTransaction.DiscardCommitted(transaction);
                throw;
            }
            await AmbientSqlTransaction.RunCommittedAsync(transaction, ex => Connector?.RecordSubscriberFailure("AfterCommit", ex)).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task CreateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            try
            {
                await AtomicallyAsync(() => CreateCoreAsync(data, storeDelegate, ct), ct);
            }
            catch (Exception ex) when (DuplicateCreate.IsDuplicate(Connector, ex))
            {
                // TASK-567. Here, around the seam, so a provider's native bulk insert (COPY, multi-row INSERT) is covered
                // whether or not it overrides CreateCoreAsync (rule 12).
                throw DuplicateCreate.Refusal(typeof(T), ex);
            }
        }

        /// <summary>
        /// Core bulk create implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual async Task CreateCoreAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            foreach (var item in data)
            {
                await CreateAsync(item, storeDelegate, ct);
            }
        }

        /// <inheritdoc />
        public async Task UpdateAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            try
            {
                await AtomicallyAsync(() => UpdateCoreAsync(data, storeDelegate, ct), ct);
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
        protected virtual async Task UpdateCoreAsync(
            IEnumerable<T> data,
            StoreDataDelegate<T>? storeDelegate = null,
            CancellationToken ct = default)
        {
            // TASK-555: each item's miss is collected rather than ending the batch — the stored ones are still written.
            var missing = new List<Guid>();
            foreach (var item in data)
            {
                try
                {
                    await UpdateAsync(item, storeDelegate, ct);
                }
                catch (EntityNotFoundException ex)
                {
                    missing.AddRange(ex.Guids);
                }
            }
            EntityNotFoundException.ThrowIfAny(typeof(T), missing);
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(
            Expression<Func<T, bool>> filter,
            Action<T> updateAction,
            CancellationToken ct = default)
        {
            // SH-M023 — read-then-loop, invisible to the connector guard; see the sync overload.
            RequireFilter(filter, "update");
            // TASK-329: and RequireFilter only covers NULL. A filter that is present but reduces to every
            // row — the empty `NOT IN` of TASK-137 — passed straight through, and because this path emits
            // a SELECT followed by per-row UPDATEs keyed on the primary key, the connector's whole-table
            // guard never sees a conditionless statement either. Measured before the fix: 3 of 3 rows
            // rewritten, thrown=NONE, while this same store's Delete(filter) and
            // Update(filter, PropertyUpdate) both refused it at the connector.
            RequireBoundedFilter(filter, "update");
            var items = (await ReadAsync(filter, null, null, null, ct)).ToList();
            foreach (var item in items)
            {
                updateAction(item);
                // SH-M434 (TASK-170): a row deleted after the read no longer matches the filter, so it is skipped, as a
                // SQL UPDATE … WHERE skips it. Throwing left the earlier rows written and the later ones not.
                try { await UpdateAsync(item, ct: ct); }
                catch (Exceptions.EntityNotFoundException) { }
            }
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(
            Expression<Func<T, bool>> filter,
            PropertyUpdate<T> updates,
            CancellationToken ct = default)
        {
            RequireFilter(filter, "update");
            await UpdateInternalAsync(filter, updates, SQL.DataBase.IsExplicitAllRows(filter as LambdaExpression), ct);
        }

        /// <inheritdoc cref="DataBaseBulkStore{DB, T}.UpdateAll(PropertyUpdate{T})"/>
        public virtual Task UpdateAllAsync(PropertyUpdate<T> updates, CancellationToken ct = default)
        {
            return UpdateInternalAsync(null, updates, allowAllRows: true, ct);
        }

        /// <inheritdoc cref="DataBaseBulkStore{DB, T}.DeleteAll()"/>
        public virtual async Task DeleteAllAsync(CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            if (Connector == null) return;
            using var _tx = EnterTransactionScope();
            if (AsyncConnector != null)
                await AsyncConnector.DeleteAllAsync(typeof(T), ct);
            else
                await Task.Run(() => Connector!.DeleteAll(typeof(T)), ct);
        }

        /// <summary>
        /// TASK-329 — refuses a filter that <b>reduces</b> to every row on a destructive write.
        /// </summary>
        /// <remarks>
        /// This store does not derive from <c>AbstractAsyncBulkStore</c> (it implements
        /// <c>IAsyncBulkStore&lt;T&gt;</c> directly), so it did not inherit that class's guard and had
        /// none of its own. Rather than a third copy of the rule, both hierarchies and this store now
        /// call one producer — <c>BoundedFilterGuard</c> — and supply only the door name, which is the
        /// part that genuinely differs: an async store has no <c>DeleteAll()</c>.
        /// <para>
        /// Only the <c>Action&lt;T&gt;</c> overload needs this. The <c>PropertyUpdate&lt;T&gt;</c> and
        /// <c>Delete(filter)</c> overloads reach the connector, where SH-H002's <c>AddRequiredWhere</c>
        /// already refuses the same shape — measured, so this is not an assumption.
        /// </para>
        /// </remarks>
        private static void RequireBoundedFilter(Expression<Func<T, bool>>? filter, string operation)
        {
            Data.Expressions.BoundedFilterGuard.Require(
                filter, operation, typeof(T).Name,
                operation == "delete" ? "DeleteAllAsync()" : "UpdateAllAsync(updates)");
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
                        + $"row. To target every row deliberately use {(operation == "delete" ? "DeleteAllAsync()" : "UpdateAllAsync(updates)")} "
                        + "or an explicit `x => true` filter.");
            }
        }

        /// <summary>
        /// Shared body of the filter overload and <see cref="UpdateAllAsync"/>.
        /// </summary>
        private async Task UpdateInternalAsync(
            Expression<Func<T, bool>>? filter,
            PropertyUpdate<T> updates,
            bool allowAllRows,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            if (Connector == null || updates.Assignments.Count == 0) return;
            using var _tx = EnterTransactionScope();

            var table = SQL.DataBase.LoadTable(typeof(T));
            var (fields, values) = PropertyUpdateSqlTranslator.Translate(updates, Connector);
            var conditions = SQL.DataBase.ParseConditionExpression(filter as LambdaExpression);
            if (AsyncConnector != null)
                await AsyncConnector.UpdateAsync(table.Name, fields, values, conditions, true, ct, allowAllRows);
            else
                await Task.Run(() => Connector!.Update(table.Name, fields, values, conditions, true, allowAllRows), ct);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(
            IEnumerable<T> data,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await AtomicallyAsync(() => DeleteCoreAsync(data, ct), ct);
        }

        /// <summary>
        /// Core bulk delete implementation. Override in concrete stores for provider-specific behavior.
        /// </summary>
        protected virtual async Task DeleteCoreAsync(
            IEnumerable<T> data,
            CancellationToken ct = default)
        {
            foreach (var item in data)
            {
                await DeleteAsync(item, ct);
            }
        }

        /// <inheritdoc />
        public virtual async Task DeleteAsync(
            Expression<Func<T, bool>> filter,
            CancellationToken ct = default)
        {
            RequireFilter(filter, "delete");
            if (SQL.DataBase.IsExplicitAllRows(filter as LambdaExpression))
            {
                await DeleteAllAsync(ct);
                return;
            }
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            if (Connector == null) return;
            using var _tx = EnterTransactionScope();
            if (AsyncConnector != null)
                await AsyncConnector.DeleteAsync(typeof(T), filter as LambdaExpression, ct);
            else
                await Task.Run(() => Connector!.Delete(typeof(T), filter as LambdaExpression), ct);
        }

        #endregion

        #region Aggregation

        /// <summary>
        /// Executes an aggregation query using SQL GROUP BY.
        /// </summary>
        public async Task<IReadOnlyList<AggregateResult>> AggregateAsync(
            AggregateQuery<T> query,
            CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            if (Connector == null)
                return Array.Empty<AggregateResult>();
            using var _tx = EnterTransactionScope();

            // SH-M034 (TASK-170): a connector without the async API answered no groups at all; every other async path
            // falls back to the sync one, and so does this.
            if (AsyncConnector == null)
                return await Task.Run(() => (IReadOnlyList<AggregateResult>)Connector.SelectAggregate(typeof(T), query).ToList().AsReadOnly(), ct).ConfigureAwait(false);

            var results = new List<AggregateResult>();
            await foreach (var row in AsyncConnector.SelectAggregateAsync(typeof(T), query, ct))
            {
                results.Add(row);
            }
            return results.AsReadOnly();
        }

        #endregion
    }
}

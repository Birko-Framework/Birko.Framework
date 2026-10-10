using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.SQL.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;

namespace Birko.Data.EventSourcing.SQL
{
    /// <summary>
    /// SQL-backed <see cref="IAsyncEventStore"/> on any provider (SQLite, PostgreSQL, SQL Server, MySQL). Events
    /// live in the <c>__Events</c> table, one row each.
    /// </summary>
    /// <typeparam name="DB">The SQL connector type (SqLiteConnector, PostgreSQLConnector, …).</typeparam>
    /// <remarks>
    /// <para>
    /// TASK-529. <see cref="IAsyncEventStore"/> had no implementation, so every consumer wrote its own; DraCode's
    /// (on SQLite) is what this replaces, minus its defects:
    /// </para>
    /// <list type="bullet">
    /// <item><b>An append-range is one transaction</b> (rule 6) — a batch that fails part-way leaves nothing behind.
    /// It runs in a <c>SqlUnitOfWork</c>, which <b>joins</b> a caller's enclosing boundary on the same database, so
    /// an entity write and its events can commit together.</item>
    /// <item><b>(AggregateGuid, Version) is unique in the database</b>, so two writers appending the same version
    /// cannot both succeed, across processes. The loser gets an <see cref="EventStoreConcurrencyException"/> and
    /// its whole batch is rolled back. The index is created by an <b>explicit</b> call on first use, which throws
    /// if it cannot be built — lazy schema-ensure would only record the failure (rule 49), and a concurrency guard
    /// that is silently absent is the § rule-51 shape.</item>
    /// <item><b>The current version is a <c>MAX(Version)</c> query</b>, not a read of every event.</item>
    /// </list>
    /// <para>
    /// ⚠ If the caller's boundary is the enclosing one, a conflict marks it rollback-only: the events were part of
    /// it, so committing the rest would apply the write without the events that describe it.
    /// </para>
    /// </remarks>
    public class SqlEventStore<DB> : IAsyncEventStore
        where DB : AbstractConnector
    {
        private readonly AsyncDataBaseBulkStore<DB, DomainEventModel> _store;
        private int _uniqueIndexEnsured;

        /// <summary>Creates an event store over the given connection settings (server SQL).</summary>
        public SqlEventStore(SqlSettings settings)
        {
            _store = new AsyncDataBaseBulkStore<DB, DomainEventModel>();
            _store.SetSettings(settings);
        }

        /// <summary>Creates an event store over a pre-configured store instance (SQLite passes one in).</summary>
        public SqlEventStore(AsyncDataBaseBulkStore<DB, DomainEventModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>The underlying store, for transaction contexts.</summary>
        public AsyncDataBaseBulkStore<DB, DomainEventModel> Store => _store;

        /// <inheritdoc />
        public Task AppendAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            return AppendRangeAsync(new[] { @event }, cancellationToken);
        }

        /// <inheritdoc />
        /// <exception cref="EventStoreConcurrencyException">An event's (aggregate, version) is already taken —
        /// nothing from the batch is written.</exception>
        public async Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var batch = events.ToList();
            if (batch.Count == 0) return;
            if (batch.Any(e => e == null)) throw new ArgumentException("An event in the batch is null.", nameof(events));

            await EnsureUniqueIndexAsync(cancellationToken).ConfigureAwait(false);
            var models = batch.Select(DomainEventModel.FromEvent).ToList();

            await using var uow = SqlUnitOfWork.FromStore(_store);
            await uow.BeginAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _store.CreateAsync(models, ct: cancellationToken).ConfigureAwait(false);
                await uow.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (_store.Connector?.IsUniqueViolationException(ex) == true)
            {
                await uow.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                var conflict = await FindConflictAsync(batch, cancellationToken).ConfigureAwait(false);
                throw new EventStoreConcurrencyException(conflict.AggregateGuid, conflict.Version, ex);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<IEvent>> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default)
            => ToEvents(await _store.ReadAsync(e => e.AggregateGuid == aggregateId, ByVersion, ct: cancellationToken).ConfigureAwait(false));

        /// <inheritdoc />
        public async Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken cancellationToken = default)
            => ToEvents(await _store.ReadAsync(e => e.AggregateGuid == aggregateId && e.Version <= maxVersion, ByVersion, ct: cancellationToken).ConfigureAwait(false));

        /// <inheritdoc />
        public async Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default)
            => ToEvents(await _store.ReadAsync(e => e.AggregateGuid == aggregateId && e.Version >= fromVersion, ByVersion, ct: cancellationToken).ConfigureAwait(false));

        /// <inheritdoc />
        /// <remarks>One <c>MAX(Version)</c> query over the aggregate's rows; 0 when it has none.</remarks>
        public async Task<long> GetVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            var rows = await _store.AggregateAsync(new AggregateQuery<DomainEventModel>
            {
                Filter = e => e.AggregateGuid == aggregateId,
                Aggregates = new[] { new AggregateField(AggregateFunction.Max, nameof(DomainEventModel.Version), "MaxVersion") },
            }, cancellationToken).ConfigureAwait(false);
            var max = rows.Count == 0 ? null : rows[0].Values.GetValueOrDefault("MaxVersion");
            return max == null || max is DBNull ? 0 : Convert.ToInt64(max);
        }

        /// <inheritdoc />
        /// <remarks>Events with <c>OccurredAt &gt;= from</c>, oldest first, then by aggregate and version.</remarks>
        public async Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken cancellationToken = default)
            => ToEvents(await _store.ReadAsync(
                e => e.OccurredAt >= from,
                OrderBy<DomainEventModel>.By(e => e.OccurredAt).ThenBy(e => e.AggregateGuid).ThenBy(e => e.Version),
                ct: cancellationToken).ConfigureAwait(false));

        private static OrderBy<DomainEventModel> ByVersion => OrderBy<DomainEventModel>.By(e => e.Version);

        private static IEnumerable<IEvent> ToEvents(IEnumerable<DomainEventModel> rows)
            => rows.Select(r => (IEvent)r.ToEvent()).ToList();

        // The unique index is the concurrency guard, so it must EXIST, not merely have been attempted: lazy
        // schema-ensure records an index it could not build and carries on (TASK-204). The explicit call
        // tolerates "already there" on every provider and throws for anything else.
        private async Task EnsureUniqueIndexAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _uniqueIndexEnsured) == 1) return;
            await _store.InitAsync(ct).ConfigureAwait(false);
            var connector = _store.Connector
                ?? throw new InvalidOperationException("The event store's connector is not configured. Call SetSettings() first.");
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(DomainEventModel));
            var index = table.Indexes?.Values.SingleOrDefault(i => i.Name == DomainEventModel.UniqueIndexName)
                ?? throw new InvalidOperationException($"{DomainEventModel.UniqueIndexName} is not declared on {nameof(DomainEventModel)}.");
            if (connector is AbstractAsyncConnector asyncConnector)
                await asyncConnector.CreateIndexesAsync(table.Name, new[] { index }, ct).ConfigureAwait(false);
            else
                await Task.Run(() => connector.CreateIndexes(table.Name, new[] { index }), ct).ConfigureAwait(false);
            Volatile.Write(ref _uniqueIndexEnsured, 1);
        }

        // Which (aggregate, version) collided: one already stored, else a duplicate inside the batch itself. Only
        // for the message — when this append joined a caller's boundary, PostgreSQL has aborted that transaction
        // and refuses the lookup, so a failed lookup falls back rather than replacing the concurrency exception.
        private async Task<(Guid AggregateGuid, long Version)> FindConflictAsync(List<IEvent> batch, CancellationToken ct)
        {
            try
            {
                foreach (var e in batch)
                {
                    var aggregate = e.AggregateGuid;
                    var version = e.Version;
                    if (await _store.CountAsync(m => m.AggregateGuid == aggregate && m.Version == version, ct).ConfigureAwait(false) > 0)
                        return (aggregate, version);
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
            }
            var duplicate = batch.GroupBy(e => (e.AggregateGuid, e.Version)).FirstOrDefault(g => g.Count() > 1);
            var first = duplicate?.First() ?? batch[0];
            return (first.AggregateGuid, first.Version);
        }
    }
}

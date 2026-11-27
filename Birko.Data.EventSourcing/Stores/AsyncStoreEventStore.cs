using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.Exceptions;
using Birko.Data.Stores;

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// The default <see cref="IAsyncEventStore"/> over any <see cref="IAsyncBulkStore{T}"/> of <see cref="EventRecord"/>
    /// (TASK-995): InMemory, MongoDB, Elasticsearch, RavenDB, Cosmos DB, or anything else a factory hands out. Usually
    /// obtained with <c>factory.GetEventStore()</c>, which picks a platform's override when one is registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Concurrency without a backend index.</b> A record's id is <see cref="EventRecord.IdFor"/> of (aggregate, version),
    /// so a second writer of one version creates an id that exists, and the backend refuses it
    /// (<see cref="StoreUniqueViolationException"/>, translated to <see cref="EventStoreConcurrencyException"/>). Measured
    /// atomic across two clients on SQLite, MongoDB, Elasticsearch, RavenDB and Cosmos DB. JSON and XML refuse it in one
    /// process only and are not supported.
    /// </para>
    /// <para>
    /// <b>A batch, whole or not at all, by compensation.</b> A bulk create that hits a taken id leaves nothing, the events
    /// before it, or every other event depending on the backend (measured, TASK-995). So the batch is created one event at a
    /// time, in order, and on a failure the events this batch already created are deleted before the exception is thrown.
    /// ⚠ A process that dies between those steps leaves the batch's first events stored: valid, contiguous versions, but
    /// part of one command. Backends with native atomicity override <see cref="AppendRangeCoreAsync"/>.
    /// </para>
    /// </remarks>
    public class AsyncStoreEventStore : AbstractAsyncEventStore
    {
        /// <summary>Creates the event store over <paramref name="store"/>.</summary>
        public AsyncStoreEventStore(IAsyncBulkStore<EventRecord> store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>The store holding the records.</summary>
        protected IAsyncBulkStore<EventRecord> Store { get; }

        /// <inheritdoc />
        protected override async Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            var written = new List<EventRecord>(batch.Count);
            foreach (var e in batch)
            {
                var record = EventRecord.FromEvent(e);
                try
                {
                    await Store.CreateAsync(record, ct: ct).ConfigureAwait(false);
                }
                catch (StoreUniqueViolationException ex)
                {
                    await CompensateAsync(written, ex).ConfigureAwait(false);
                    throw new EventStoreConcurrencyException(e.AggregateGuid, e.Version, ex);
                }
                catch (Exception ex)
                {
                    await CompensateAsync(written, ex).ConfigureAwait(false);
                    throw;
                }
                written.Add(record);
            }
        }

        /// <summary>
        /// Deletes the records a failed batch already created. When that fails too, the batch is partly stored and the
        /// caller must know it: the exception says which versions remain, carrying both failures.
        /// </summary>
        protected async Task CompensateAsync(IReadOnlyList<EventRecord> written, Exception cause)
        {
            if (written.Count == 0) return;
            try
            {
                await Store.DeleteAsync(written, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception undo)
            {
                var kept = string.Join(", ", written.Select(r => $"{r.AggregateGuid}@{r.Version}"));
                throw new InvalidOperationException(
                    $"An event batch failed and its stored events could not be removed; these remain: {kept}.",
                    new AggregateException(cause, undo));
            }
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<IEvent>> ReadCoreAsync(Guid aggregateId, long? fromVersion, long? toVersion, CancellationToken ct)
        {
            Expression<Func<EventRecord, bool>> filter = (fromVersion, toVersion) switch
            {
                (null, null) => r => r.AggregateGuid == aggregateId,
                ({ } from, null) => r => r.AggregateGuid == aggregateId && r.Version >= from,
                (null, { } to) => r => r.AggregateGuid == aggregateId && r.Version <= to,
                ({ } from, { } to) => r => r.AggregateGuid == aggregateId && r.Version >= from && r.Version <= to,
            };
            return ToEvents(await Store.ReadAsync(filter, OrderBy<EventRecord>.By(r => r.Version), ct: ct).ConfigureAwait(false));
        }

        /// <inheritdoc />
        protected override async Task<long> GetVersionCoreAsync(Guid aggregateId, CancellationToken ct)
        {
            var top = await Store.ReadAsync(r => r.AggregateGuid == aggregateId, OrderBy<EventRecord>.ByDescending(r => r.Version), 1, ct: ct)
                .ConfigureAwait(false);
            return top.FirstOrDefault()?.Version ?? 0;
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<IEvent>> ReadAllFromCoreAsync(DateTime from, CancellationToken ct)
            => ToEvents(await Store.ReadAsync(
                r => r.OccurredAt >= from,
                OrderBy<EventRecord>.By(r => r.OccurredAt).ThenBy(r => r.AggregateGuid).ThenBy(r => r.Version),
                ct: ct).ConfigureAwait(false));

        /// <summary>The records as events, in the order given.</summary>
        protected static IEnumerable<IEvent> ToEvents(IEnumerable<EventRecord> records)
            => records.Select(r => (IEvent)r.ToEvent()).ToList();
    }
}

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
    /// <b>A batch, whole or not at all.</b> A bulk create that hits a taken id leaves nothing, the events before it, or
    /// every other event depending on the backend (measured, TASK-995). So the batch is created one event at a time, in
    /// order. Backends with native atomicity override <see cref="AppendRangeCoreAsync"/>; here (TASK-1000, the user's
    /// decisions):
    /// </para>
    /// <list type="bullet">
    /// <item>every record of a batch of two or more carries the batch's id, its size, and the id of its last event
    /// (<see cref="EventRecord.BatchGuid"/>). A single event carries none and stays one write;</item>
    /// <item>reads show a batch only once all its records are stored, so a batch cut short by a crash is invisible;</item>
    /// <item>an append that collides with an incomplete batch older than <see cref="AbandonedBatchLease"/> clears it and
    /// retries. A younger one may belong to a writer still running, and is a conflict. Clearing first stores an abort
    /// marker under the id of the batch's last event: that create and the writer's own last create exclude each other, so
    /// the batch either completes or is cleared, never both;</item>
    /// <item>a failed batch removes its records by batch id, never by record id, so it cannot remove a version another
    /// writer took after its batch was cleared; and a writer checks, after its last create, that its batch is whole.</item>
    /// </list>
    /// <para>
    /// The lease is compared with the records' write times from the writers' clocks, so clocks that differ by more than
    /// the lease can clear a batch that is still being written; that writer then fails rather than reporting success.
    /// </para>
    /// </remarks>
    public class AsyncStoreEventStore : AbstractAsyncEventStore
    {
        // An incomplete batch is judged from the rows of one aggregate at the top of its stream; this many are read at a time.
        private const int VersionPage = 16;

        private TimeSpan _abandonedBatchLease = TimeSpan.FromSeconds(30);

        /// <summary>Creates the event store over <paramref name="store"/>.</summary>
        public AsyncStoreEventStore(IAsyncBulkStore<EventRecord> store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>The store holding the records.</summary>
        protected IAsyncBulkStore<EventRecord> Store { get; }

        /// <summary>
        /// How long an incomplete batch must sit, since its newest record was written, before an append may clear it as
        /// abandoned (TASK-1000; default 30 s). A writer slower than this between two events of one batch loses the batch and
        /// gets <see cref="EventStoreConcurrencyException"/>.
        /// </summary>
        public TimeSpan AbandonedBatchLease
        {
            get => _abandonedBatchLease;
            set => _abandonedBatchLease = value >= TimeSpan.Zero
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "The lease cannot be negative.");
        }

        /// <summary>
        /// Makes every write so far visible to the store's queries. A no-op where a query sees a write at once; Elasticsearch
        /// refreshes its index.
        /// </summary>
        protected virtual Task MakeVisibleAsync(CancellationToken ct) => Task.CompletedTask;

        /// <inheritdoc />
        protected override async Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            var records = batch.Select(EventRecord.FromEvent).ToList();
            Guid? batchGuid = null;
            if (records.Count > 1)
            {
                batchGuid = Guid.NewGuid();
                var end = records[^1].Guid;
                foreach (var r in records)
                {
                    r.BatchGuid = batchGuid;
                    r.BatchSize = records.Count;
                    r.BatchEnd = end;
                }
            }

            var written = new List<EventRecord>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                try
                {
                    await CreateClearingAbandonedAsync(record, ct).ConfigureAwait(false);
                }
                catch (StoreUniqueViolationException ex)
                {
                    await CompensateAsync(written, batchGuid, ex).ConfigureAwait(false);
                    throw new EventStoreConcurrencyException(batch[i].AggregateGuid, batch[i].Version, ex);
                }
                catch (Exception ex)
                {
                    await CompensateAsync(written, batchGuid, ex).ConfigureAwait(false);
                    throw;
                }
                written.Add(record);
            }

            if (batchGuid is not { } own) return;
            // Another writer may have judged this batch abandoned while it was being written and cleared its first records;
            // then the last create succeeded on a batch that can never be whole. Reported, never returned as success.
            bool whole;
            try
            {
                await MakeVisibleAsync(ct).ConfigureAwait(false);
                whole = await IsCompleteAsync(own, records.Count, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await CompensateAsync(written, batchGuid, ex).ConfigureAwait(false);
                throw;
            }
            if (whole) return;
            var cleared = new InvalidOperationException(
                $"The event batch {own} was cleared as abandoned by another writer before it completed.");
            await CompensateAsync(written, batchGuid, cleared).ConfigureAwait(false);
            var last = batch[^1];
            throw new EventStoreConcurrencyException(last.AggregateGuid, last.Version, cleared);
        }

        // Creates the record; when its id is held by an abandoned batch, clears that batch and creates once more.
        private async Task CreateClearingAbandonedAsync(EventRecord record, CancellationToken ct)
        {
            record.WrittenAt = DateTime.UtcNow;
            try
            {
                await Store.CreateAsync(record, ct: ct).ConfigureAwait(false);
                return;
            }
            catch (StoreUniqueViolationException)
            {
                if (!await TryClearAbandonedAsync(record.Guid!.Value, record.BatchGuid, ct).ConfigureAwait(false)) throw;
            }
            record.WrittenAt = DateTime.UtcNow;
            await Store.CreateAsync(record, ct: ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether the record holding <paramref name="takenId"/> was part of an abandoned batch, now cleared, so the id is
        /// free to take. False for a stored event, a whole batch, a batch younger than the lease, and this writer's own batch.
        /// </summary>
        private async Task<bool> TryClearAbandonedAsync(Guid takenId, Guid? ownBatch, CancellationToken ct)
        {
            await MakeVisibleAsync(ct).ConfigureAwait(false);
            var taken = await Store.ReadAsync(takenId, ct).ConfigureAwait(false);
            if (taken == null) return true; // removed in the meantime
            if (taken.BatchGuid is not { } batch || batch == ownBatch) return false;

            // Twice at most: once more when the batch's last event appeared while this writer was claiming it.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var members = await BatchMembersAsync(batch, ct).ConfigureAwait(false);
                if (members.Count == 0) return true;
                if (members.Any(m => m.Aborted))
                {
                    await DeleteBatchAsync(batch, ct).ConfigureAwait(false);
                    return true;
                }
                var size = members[0].BatchSize;
                if (members.Count >= size) return false;
                var newest = members.Max(m => m.WrittenAt ?? DateTime.MinValue);
                if (DateTime.UtcNow - newest < AbandonedBatchLease) return false;

                var end = members[0].BatchEnd ?? takenId;
                var marker = new EventRecord
                {
                    Guid = end,
                    BatchGuid = batch,
                    BatchSize = size,
                    BatchEnd = end,
                    Aborted = true,
                    WrittenAt = DateTime.UtcNow,
                };
                try
                {
                    await Store.CreateAsync(marker, ct: ct).ConfigureAwait(false);
                }
                catch (StoreUniqueViolationException)
                {
                    // the batch's last event exists: judge the batch again
                    await MakeVisibleAsync(ct).ConfigureAwait(false);
                    continue;
                }
                await DeleteBatchAsync(batch, ct).ConfigureAwait(false);
                return true;
            }
            // The last event exists but the batch is not whole: its writer finds that and fails. Not cleared here.
            return false;
        }

        private async Task<List<EventRecord>> BatchMembersAsync(Guid batch, CancellationToken ct)
            => (await Store.ReadAsync(r => r.BatchGuid == batch, ct: ct).ConfigureAwait(false)).ToList();

        private async Task<bool> IsCompleteAsync(Guid batch, int size, CancellationToken ct)
        {
            var members = await BatchMembersAsync(batch, ct).ConfigureAwait(false);
            return members.Count == size && !members.Any(m => m.Aborted);
        }

        private async Task DeleteBatchAsync(Guid batch, CancellationToken ct)
        {
            await MakeVisibleAsync(ct).ConfigureAwait(false);
            await Store.DeleteAsync(r => r.BatchGuid == batch, ct).ConfigureAwait(false);
            await MakeVisibleAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the records a failed batch already created: by batch id for a marked batch, so only this batch's records
        /// go. When that fails too, the batch is partly stored (invisible, and cleared by a later append after the lease) and
        /// the caller must know it: the exception says which versions remain, carrying both failures.
        /// </summary>
        protected async Task CompensateAsync(IReadOnlyList<EventRecord> written, Guid? batchGuid, Exception cause)
        {
            if (written.Count == 0) return;
            try
            {
                if (batchGuid is { } batch)
                    await DeleteBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
                else
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

        /// <summary>
        /// <paramref name="records"/> without abort markers and without the records of a batch that is not whole, in the
        /// order given. A batch whose records are all in hand costs nothing; any other one is looked up once.
        /// </summary>
        protected async Task<List<EventRecord>> VisibleAsync(IReadOnlyList<EventRecord> records, CancellationToken ct)
        {
            HashSet<Guid>? hidden = null;
            foreach (var group in records.Where(r => r.BatchGuid != null).GroupBy(r => r.BatchGuid!.Value))
            {
                var size = group.First().BatchSize;
                var inHand = !group.Any(r => r.Aborted) && group.Count() == size;
                if (inHand || await IsCompleteAsync(group.Key, size, ct).ConfigureAwait(false)) continue;
                (hidden ??= new HashSet<Guid>()).Add(group.Key);
            }
            return records.Where(r => !r.Aborted && (r.BatchGuid is not { } b || hidden == null || !hidden.Contains(b))).ToList();
        }

        /// <summary>
        /// Which (aggregate, version) a batch refused as a whole collided on, for an override whose backend writes a batch
        /// atomically and does not say which event conflicted: a version twice in the batch, else one already stored, else
        /// the first event.
        /// </summary>
        /// <remarks>
        /// Only for the exception's message. When the append ran inside a caller's transaction that the conflict aborted,
        /// the lookup can be refused too; it then falls back to the first event rather than replace the concurrency
        /// exception, as <c>SqlEventStore</c>'s lookup does.
        /// </remarks>
        protected async Task<(Guid AggregateGuid, long Version)> FindConflictAsync(IReadOnlyList<IEvent> batch, CancellationToken ct)
        {
            if (DuplicateInBatch(batch) is { } duplicate) return duplicate;
            try
            {
                foreach (var e in batch)
                {
                    var id = EventRecord.IdFor(e.AggregateGuid, e.Version);
                    if (await Store.CountAsync(r => r.Guid == id, ct).ConfigureAwait(false) > 0) return (e.AggregateGuid, e.Version);
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
            }
            return (batch[0].AggregateGuid, batch[0].Version);
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
            var records = (await Store.ReadAsync(filter, OrderBy<EventRecord>.By(r => r.Version), ct: ct).ConfigureAwait(false)).ToList();
            return ToEvents(await VisibleAsync(records, ct).ConfigureAwait(false));
        }

        /// <inheritdoc />
        /// <remarks>
        /// The highest version of a visible record, judged from the top down: it stops at the first visible one, so only
        /// the batches above it are checked, and a whole batch in the page read costs no lookup.
        /// </remarks>
        protected override async Task<long> GetVersionCoreAsync(Guid aggregateId, CancellationToken ct)
        {
            var judged = new Dictionary<Guid, bool>();
            for (var offset = 0; ; offset += VersionPage)
            {
                var top = (await Store.ReadAsync(r => r.AggregateGuid == aggregateId, OrderBy<EventRecord>.ByDescending(r => r.Version),
                    VersionPage, offset, ct).ConfigureAwait(false)).ToList();
                foreach (var record in top)
                {
                    if (record.Aborted) continue;
                    if (record.BatchGuid is not { } batch) return record.Version;
                    if (!judged.TryGetValue(batch, out var whole))
                    {
                        var inPage = top.Where(r => r.BatchGuid == batch).ToList();
                        whole = (!inPage.Any(r => r.Aborted) && inPage.Count == record.BatchSize)
                                || await IsCompleteAsync(batch, record.BatchSize, ct).ConfigureAwait(false);
                        judged[batch] = whole;
                    }
                    if (whole) return record.Version;
                }
                if (top.Count < VersionPage) return 0;
            }
        }

        /// <inheritdoc />
        protected override async Task<IEnumerable<IEvent>> ReadAllFromCoreAsync(DateTime from, CancellationToken ct)
        {
            var records = (await Store.ReadAsync(
                r => r.OccurredAt >= from,
                OrderBy<EventRecord>.By(r => r.OccurredAt).ThenBy(r => r.AggregateGuid).ThenBy(r => r.Version),
                ct: ct).ConfigureAwait(false)).ToList();
            return ToEvents(await VisibleAsync(records, ct).ConfigureAwait(false));
        }

        /// <summary>The records as events, in the order given.</summary>
        protected static IEnumerable<IEvent> ToEvents(IEnumerable<EventRecord> records)
            => records.Select(r => (IEvent)r.ToEvent()).ToList();
    }
}

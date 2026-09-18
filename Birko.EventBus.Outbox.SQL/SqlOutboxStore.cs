using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.EventBus.Outbox.SQL.Models;

namespace Birko.EventBus.Outbox.SQL
{
    /// <summary>
    /// SQL-backed <see cref="IOutboxStore"/>. Entries survive process restarts, and concurrent
    /// processors cannot publish the same entry twice.
    /// </summary>
    /// <typeparam name="DB">The SQL connector type (SqLiteConnector, PostgreSQLConnector, …).</typeparam>
    /// <remarks>
    /// <para>
    /// <c>InMemoryOutboxStore</c> was the only implementation shipped, so the transactional-outbox
    /// guarantee — an event is published if and only if the write that produced it survived — was
    /// defeated by the first crash. An outbox that does not outlive the process is a queue with extra
    /// steps.
    /// </para>
    /// <para>
    /// <b>Claimed, not just read.</b> <see cref="GetPendingAsync"/> marks each entry it returns with a
    /// token before handing it over, so a second processor polling the same table gets a different batch.
    /// Without that, two processors read the same pending rows and every event is published twice — the
    /// mirror of the race <c>SqlJobQueue</c> closes on dequeue, and the reason
    /// <see cref="OutboxEntry.ClaimedAt"/> already exists on the core contract.
    /// </para>
    /// <para>
    /// ⚠ <b>Delivery is at-least-once, and that is inherent rather than a shortcut.</b> An entry can be
    /// claimed, published, and then fail to be marked published — a crash in that window republishes it
    /// on recovery. The alternative (mark first, publish second) loses events instead, which is strictly
    /// worse. Consumers must therefore tolerate a repeat; the framework's own event de-duplication is
    /// the intended answer, and handlers that write rows need to be idempotent regardless.
    /// </para>
    /// <para>
    /// ⚠ <b>A claim is not a lease.</b> If a processor dies mid-batch its claimed entries stay
    /// <c>Publishing</c> and no one retries them, because nothing here knows the holder is gone. That is
    /// a deliberate limit of this first version, not an oversight — reclaiming needs either a heartbeat
    /// or an age-based sweep, and both need a policy decision about how long "too long" is.
    /// </para>
    /// </remarks>
    public class SqlOutboxStore<DB> : IOutboxStore
        where DB : AbstractConnector
    {
        private readonly AsyncDataBaseBulkStore<DB, OutboxEntryModel> _store;

        /// <summary>
        /// How long an entry may sit in <c>Publishing</c> before it is presumed abandoned and offered
        /// again.
        /// </summary>
        /// <remarks>
        /// Five minutes, and unlike the job queue's equivalent this is a CHOSEN bound rather than a
        /// derived one — <c>OutboxOptions</c> declares no publish timeout to read it off. Publishing is an
        /// in-process dispatch to handlers, orders of magnitude shorter than five minutes in anything
        /// observed, so the margin is generous. A handler that legitimately runs longer WILL have its
        /// entry republished; that is within the at-least-once contract this store already documents, but
        /// it is a real consequence rather than a theoretical one.
        /// </remarks>
        private readonly TimeSpan _claimTimeout;

        /// <summary>
        /// Attempts before a repeatedly-reclaimed entry is given up on. Defaults to
        /// <c>OutboxOptions.MaxAttempts</c>'s own default so the two do not silently disagree.
        /// </summary>
        private readonly int _maxAttempts;

        /// <summary>Creates a store over the given connection settings.</summary>
        public SqlOutboxStore(SqlSettings settings, TimeSpan? claimTimeout = null, int maxAttempts = 5)
        {
            _store = new AsyncDataBaseBulkStore<DB, OutboxEntryModel>();
            _store.SetSettings(settings);
            _claimTimeout = claimTimeout ?? TimeSpan.FromMinutes(5);
            _maxAttempts = maxAttempts;
        }

        /// <summary>Creates a store over a pre-configured store instance (SQLite passes one in).</summary>
        public SqlOutboxStore(AsyncDataBaseBulkStore<DB, OutboxEntryModel> store, TimeSpan? claimTimeout = null, int maxAttempts = 5)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _claimTimeout = claimTimeout ?? TimeSpan.FromMinutes(5);
            _maxAttempts = maxAttempts;
        }

        /// <summary>The underlying store, for transaction contexts.</summary>
        public AsyncDataBaseBulkStore<DB, OutboxEntryModel> Store => _store;

        public async Task SaveAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            await _store.CreateAsync(OutboxEntryModel.FromEntry(entry), ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
            int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0) return Array.Empty<OutboxEntry>();

            var pending = (int)OutboxStatus.Pending;
            var publishing = (int)OutboxStatus.Publishing;

            var now = DateTime.UtcNow;

            // TASK-451: an entry left Publishing by a processor that died is offered again once it has
            // been held longer than the claim timeout. Without this it stayed Publishing forever - the
            // "a claim is not a lease" limitation this store's own remarks called out.
            var staleBefore = now - _claimTimeout;

            var candidates = await _store.ReadAsync(
                filter: e => e.Status == pending
                          || (e.Status == publishing && e.ClaimedAt != null && e.ClaimedAt < staleBefore),
                orderBy: OrderBy<OutboxEntryModel>.By(e => e.CreatedAt),
                limit: batchSize,
                ct: cancellationToken).ConfigureAwait(false);

            var claimed = new List<OutboxEntry>();

            foreach (var candidate in candidates ?? Enumerable.Empty<OutboxEntryModel>())
            {
                var id = candidate.Guid;
                var token = Guid.NewGuid();
                var origin = candidate.Status;
                var reclaiming = origin == publishing;

                // TASK-451: reclaiming has to be bounded, or an entry whose publish kills the processor
                // every time is handed round forever and the failure never surfaces.
                if (reclaiming && candidate.Attempts + 1 >= _maxAttempts)
                {
                    candidate.Attempts += 1;
                    candidate.Status = (int)OutboxStatus.Failed;
                    candidate.ClaimToken = null;
                    candidate.ClaimedAt = null;
                    candidate.LastError = $"Abandoned: held in Publishing for longer than {_claimTimeout} "
                        + $"after {candidate.Attempts} attempt(s), and the attempt budget is spent.";
                    await _store.UpdateAsync(candidate, ct: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Conditional on the row still being in the status we read it in, so the database
                // serializes the write and only one processor's WHERE can match. Same shape as
                // SqlJobQueue's dequeue claim.
                await _store.UpdateAsync(
                    filter: e => e.Guid == id && e.Status == origin,
                    updates: new PropertyUpdate<OutboxEntryModel>()
                        .Set(e => e.Status, publishing)
                        .Set(e => e.ClaimToken, token)
                        .Set(e => e.ClaimedAt, now)
                        // A redelivery is counted, or the attempt budget above could never run out.
                        .Set(e => e.Attempts, reclaiming ? candidate.Attempts + 1 : candidate.Attempts),
                    ct: cancellationToken).ConfigureAwait(false);

                // The API exposes no rows-affected count, so the token is how we learn whether we won.
                var after = await _store.ReadAsync(e => e.Guid == id, cancellationToken).ConfigureAwait(false);
                if (after != null && after.ClaimToken == token)
                {
                    claimed.Add(after.ToEntry());
                }
            }

            return claimed;
        }

        public async Task MarkPublishedAsync(Guid entryId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(e => e.Guid == entryId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.Status = (int)OutboxStatus.Published;
            model.PublishedAt = DateTime.UtcNow;
            model.ClaimToken = null;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Records a failed publish. Below <paramref name="maxAttempts"/> the entry returns to
        /// <c>Pending</c> for another try; at the cap it becomes <c>Failed</c> and stops being retried.
        /// </summary>
        /// <remarks>
        /// The cap is a parameter rather than a constant here because the core contract passes it from
        /// <c>OutboxOptions.MaxAttempts</c> — one place to configure it, not one per store. Reaching it is
        /// terminal on purpose: an event whose handler always throws would otherwise be retried for the
        /// life of the deployment, and the failure would never surface anywhere.
        /// </remarks>
        public async Task MarkFailedAsync(
            Guid entryId, string error, int maxAttempts, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(e => e.Guid == entryId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.Attempts += 1;
            model.LastError = error?.Length > 2000 ? error[..2000] : error;
            model.ClaimToken = null;
            model.ClaimedAt = null;
            model.Status = model.Attempts >= maxAttempts
                ? (int)OutboxStatus.Failed
                : (int)OutboxStatus.Pending;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes settled entries older than the cutoff. <c>Pending</c> and <c>Publishing</c> rows are
        /// never removed — an unpublished event is the thing this table exists to protect, and deleting
        /// one because it is old would lose it precisely when something is already wrong.
        /// </summary>
        public async Task CleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
        {
            var published = (int)OutboxStatus.Published;
            var failed = (int)OutboxStatus.Failed;

            await _store.DeleteAsync(
                filter: e => e.CreatedAt < cutoffDate && (e.Status == published || e.Status == failed),
                ct: cancellationToken).ConfigureAwait(false);
        }
    }
}

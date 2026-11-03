using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
    /// <b>A claim is a lease.</b> An entry held in <c>Publishing</c> longer than the claim timeout is
    /// reclaimed (TASK-451), and the reclaim is won by at most one processor (TASK-710). A mark carries
    /// the claim it was handed out under and applies only while that claim is current, so a slow
    /// processor whose entry was reclaimed cannot put it back to <c>Pending</c> or over a newer outcome.
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
        /// The attempt cap for <see cref="GetPendingAsync(int, CancellationToken)"/> only, a caller with no
        /// <c>OutboxOptions</c>. The processor passes <c>OutboxOptions.MaxAttempts</c> through
        /// <see cref="GetPendingAsync(int, int, CancellationToken)"/>, so the two cannot disagree (TASK-710).
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

        /// <summary>Claims a batch with the attempt cap this store was constructed with.</summary>
        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
            int batchSize, CancellationToken cancellationToken = default)
            => GetPendingAsync(batchSize, _maxAttempts, cancellationToken);

        public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
            int batchSize, int maxAttempts, CancellationToken cancellationToken = default)
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
                var reclaiming = candidate.Status == publishing;
                var seenAttempts = candidate.Attempts;

                // Conditional on the row still being as we read it, so the database serializes the write
                // and only one processor's WHERE can match. TASK-710: Status alone could not tell. A
                // reclaim writes Publishing over Publishing, so a second processor's reclaim matched after
                // the first one's and both published (measured); the winner's write moves ClaimedAt to
                // now, so "still stale" matches one reclaim at most. A Pending claim also requires the
                // attempts it read: an entry claimed and failed back to Pending in between matched, and
                // the late claim wound Attempts back (measured). As SqlJobQueue (TASK-575, TASK-158).
                Expression<Func<OutboxEntryModel, bool>> stillEligible = reclaiming
                    ? e => e.Guid == id && e.Status == publishing && e.ClaimedAt != null && e.ClaimedAt < staleBefore
                    : e => e.Guid == id && e.Status == pending && e.Attempts == seenAttempts;

                // TASK-451: reclaiming has to be bounded, or an entry whose publish kills the processor
                // every time is handed round forever and the failure never surfaces. TASK-710: guarded
                // like the claim; it wrote the whole row back, over whatever had happened since the read.
                if (reclaiming && seenAttempts + 1 >= maxAttempts)
                {
                    await _store.UpdateAsync(
                        filter: stillEligible,
                        updates: new PropertyUpdate<OutboxEntryModel>()
                            .Set(e => e.Status, (int)OutboxStatus.Failed)
                            .Set(e => e.Attempts, seenAttempts + 1)
                            .Set(e => e.ClaimToken, (Guid?)null)
                            .Set(e => e.ClaimedAt, (DateTime?)null)
                            .Set(e => e.LastError, $"Abandoned: held in Publishing for longer than {_claimTimeout} "
                                + $"after {seenAttempts + 1} attempt(s), and the attempt budget is spent."),
                        ct: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _store.UpdateAsync(
                    filter: stillEligible,
                    updates: new PropertyUpdate<OutboxEntryModel>()
                        .Set(e => e.Status, publishing)
                        .Set(e => e.ClaimToken, token)
                        .Set(e => e.ClaimedAt, now)
                        // A redelivery is counted, or the attempt budget above could never run out.
                        .Set(e => e.Attempts, reclaiming ? seenAttempts + 1 : seenAttempts),
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

        // TASK-710: each mark is one update guarded by the entry's claim. They read the row by id and wrote it back, so a
        // processor whose entry had been reclaimed put it back to Pending while the new holder was publishing it (a third
        // claim followed), or turned a Published entry back to Pending (both measured).
        public async Task MarkPublishedAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            var (id, token) = Claim(entry);
            var publishing = (int)OutboxStatus.Publishing;

            await _store.UpdateAsync(
                filter: e => e.Guid == id && e.Status == publishing && e.ClaimToken == token,
                updates: new PropertyUpdate<OutboxEntryModel>()
                    .Set(e => e.Status, (int)OutboxStatus.Published)
                    .Set(e => e.PublishedAt, DateTime.UtcNow)
                    .Set(e => e.ClaimToken, (Guid?)null),
                ct: cancellationToken).ConfigureAwait(false);
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
            OutboxEntry entry, string error, int maxAttempts, CancellationToken cancellationToken = default)
        {
            var (id, token) = Claim(entry);
            var publishing = (int)OutboxStatus.Publishing;
            Expression<Func<OutboxEntryModel, bool>> held = e => e.Guid == id && e.Status == publishing && e.ClaimToken == token;

            var model = await _store.ReadAsync(held, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            var attempts = model.Attempts + 1;
            await _store.UpdateAsync(
                filter: held,
                updates: new PropertyUpdate<OutboxEntryModel>()
                    .Set(e => e.Attempts, attempts)
                    .Set(e => e.LastError, error?.Length > 2000 ? error[..2000] : error)
                    .Set(e => e.ClaimToken, (Guid?)null)
                    .Set(e => e.ClaimedAt, (DateTime?)null)
                    .Set(e => e.Status, attempts >= maxAttempts ? (int)OutboxStatus.Failed : (int)OutboxStatus.Pending),
                ct: cancellationToken).ConfigureAwait(false);
        }

        private static (Guid Id, Guid Token) Claim(OutboxEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.ClaimToken is not Guid token)
                throw new ArgumentException("The entry carries no claim token: only an entry GetPendingAsync handed out can be marked.", nameof(entry));
            return (entry.Guid, token);
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

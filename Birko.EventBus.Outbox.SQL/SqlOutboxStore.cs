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

        /// <summary>Creates a store over the given connection settings.</summary>
        public SqlOutboxStore(SqlSettings settings)
        {
            _store = new AsyncDataBaseBulkStore<DB, OutboxEntryModel>();
            _store.SetSettings(settings);
        }

        /// <summary>Creates a store over a pre-configured store instance (SQLite passes one in).</summary>
        public SqlOutboxStore(AsyncDataBaseBulkStore<DB, OutboxEntryModel> store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
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

            var candidates = await _store.ReadAsync(
                filter: e => e.Status == pending,
                orderBy: OrderBy<OutboxEntryModel>.By(e => e.CreatedAt),
                limit: batchSize,
                ct: cancellationToken).ConfigureAwait(false);

            var claimed = new List<OutboxEntry>();
            var now = DateTime.UtcNow;

            foreach (var candidate in candidates ?? Enumerable.Empty<OutboxEntryModel>())
            {
                var id = candidate.Guid;
                var token = Guid.NewGuid();

                // Conditional on the row still being Pending, so the database serializes the write and
                // only one processor's WHERE can match. Same shape as SqlJobQueue's dequeue claim.
                await _store.UpdateAsync(
                    filter: e => e.Guid == id && e.Status == pending,
                    updates: new PropertyUpdate<OutboxEntryModel>()
                        .Set(e => e.Status, publishing)
                        .Set(e => e.ClaimToken, token)
                        .Set(e => e.ClaimedAt, now),
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

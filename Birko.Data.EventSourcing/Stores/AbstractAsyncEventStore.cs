using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;

namespace Birko.Data.EventSourcing.Stores
{
    /// <summary>
    /// The shared base of every <see cref="IAsyncEventStore"/> (TASK-995), as <c>AbstractAsyncStore</c> is for stores: the
    /// public contract is non-virtual and checks its arguments once, and a platform overrides the protected <c>*Core</c>
    /// seams. <see cref="AsyncStoreEventStore"/> is the default over any store; <c>SqlEventStore</c> and the other
    /// <c>Birko.Data.EventSourcing.*</c> projects override it where their backend does better natively.
    /// </summary>
    /// <remarks>
    /// Every implementation keeps one contract: a batch is appended whole or not at all, and an (aggregate, version) that is
    /// already taken — by another writer, in another process, or twice in one batch — is refused with
    /// <see cref="EventStoreConcurrencyException"/>. Reads come back oldest version first.
    /// </remarks>
    public abstract class AbstractAsyncEventStore : IAsyncEventStore
    {
        /// <inheritdoc />
        public Task AppendAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            return AppendRangeAsync(new[] { @event }, cancellationToken);
        }

        /// <inheritdoc />
        /// <exception cref="EventStoreConcurrencyException">An event's (aggregate, version) is already taken — nothing from the
        /// batch is kept.</exception>
        public Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var batch = events.ToList();
            if (batch.Count == 0) return Task.CompletedTask;
            if (batch.Any(e => e == null)) throw new ArgumentException("An event in the batch is null.", nameof(events));
            return AppendRangeCoreAsync(batch, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IEnumerable<IEvent>> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default)
            => ReadCoreAsync(aggregateId, null, null, cancellationToken);

        /// <inheritdoc />
        public Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken cancellationToken = default)
            => ReadCoreAsync(aggregateId, null, maxVersion, cancellationToken);

        /// <inheritdoc />
        public Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default)
            => ReadCoreAsync(aggregateId, fromVersion, null, cancellationToken);

        /// <inheritdoc />
        public Task<long> GetVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default)
            => GetVersionCoreAsync(aggregateId, cancellationToken);

        /// <inheritdoc />
        /// <remarks>Events with <c>OccurredAt &gt;= from</c>, oldest first, then by aggregate and version.</remarks>
        public Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken cancellationToken = default)
            => ReadAllFromCoreAsync(from, cancellationToken);

        /// <summary>Appends a non-empty batch with no null events, whole or not at all.</summary>
        protected abstract Task AppendRangeCoreAsync(IReadOnlyList<IEvent> batch, CancellationToken ct);

        /// <summary>An aggregate's events with <paramref name="fromVersion"/> ≤ version ≤ <paramref name="toVersion"/> (a null
        /// bound is open), oldest version first.</summary>
        protected abstract Task<IEnumerable<IEvent>> ReadCoreAsync(Guid aggregateId, long? fromVersion, long? toVersion, CancellationToken ct);

        /// <summary>The aggregate's highest version; 0 when it has no events.</summary>
        protected abstract Task<long> GetVersionCoreAsync(Guid aggregateId, CancellationToken ct);

        /// <summary>Events with <c>OccurredAt &gt;= from</c>, oldest first, then by aggregate and version.</summary>
        protected abstract Task<IEnumerable<IEvent>> ReadAllFromCoreAsync(DateTime from, CancellationToken ct);

        /// <summary>The first (aggregate, version) that occurs twice in <paramref name="batch"/>, if any.</summary>
        protected static (Guid AggregateGuid, long Version)? DuplicateInBatch(IReadOnlyList<IEvent> batch)
        {
            var duplicate = batch.GroupBy(e => (e.AggregateGuid, e.Version)).FirstOrDefault(g => g.Count() > 1);
            return duplicate == null ? null : duplicate.Key;
        }
    }
}

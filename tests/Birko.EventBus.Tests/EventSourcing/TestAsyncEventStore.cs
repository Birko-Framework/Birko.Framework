using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using DomainEvent = Birko.Data.EventSourcing.Events.IEvent;

namespace Birko.EventBus.Tests.EventSourcing
{
    /// <summary>
    /// Simple in-memory async event store for testing.
    /// </summary>
    public class TestAsyncEventStore : IAsyncEventStore
    {
        private readonly ConcurrentBag<DomainEvent> _events = new();

        public Task AppendAsync(DomainEvent @event, CancellationToken cancellationToken = default)
        {
            _events.Add(@event);
            return Task.CompletedTask;
        }

        public Task AppendRangeAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (var e in events) _events.Add(e);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<DomainEvent>> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DomainEvent>>(
                _events.Where(e => e.AggregateId == aggregateId).OrderBy(e => e.Version).ToList());
        }

        public Task<IEnumerable<DomainEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DomainEvent>>(
                _events.Where(e => e.AggregateId == aggregateId && e.Version <= maxVersion).OrderBy(e => e.Version).ToList());
        }

        public Task<IEnumerable<DomainEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DomainEvent>>(
                _events.Where(e => e.AggregateId == aggregateId && e.Version >= fromVersion).OrderBy(e => e.Version).ToList());
        }

        public Task<long> GetVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default)
        {
            var max = _events.Where(e => e.AggregateId == aggregateId).Select(e => e.Version).DefaultIfEmpty(0).Max();
            return Task.FromResult(max);
        }

        public Task<IEnumerable<DomainEvent>> ReadAllFromAsync(DateTime from, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DomainEvent>>(
                _events.Where(e => e.OccurredAt >= from).OrderBy(e => e.OccurredAt).ToList());
        }
    }
}

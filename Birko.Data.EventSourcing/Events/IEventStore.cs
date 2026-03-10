using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.EventSourcing.Events
{
    /// <summary>
    /// Interface for event storage in event sourcing.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>
        /// Appends a new event to the event store.
        /// </summary>
        /// <param name="event">The event to append.</param>
        void Append(IEvent @event);

        /// <summary>
        /// Appends multiple events atomically.
        /// </summary>
        /// <param name="events">The events to append.</param>
        void AppendRange(IEnumerable<IEvent> events);

        /// <summary>
        /// Reads all events for an aggregate.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <returns>All events for the aggregate, in order.</returns>
        IEnumerable<IEvent> Read(Guid aggregateId);

        /// <summary>
        /// Reads events for an aggregate up to a specific version.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="maxVersion">The maximum version to read.</param>
        /// <returns>Events up to the specified version.</returns>
        IEnumerable<IEvent> ReadUpToVersion(Guid aggregateId, long maxVersion);

        /// <summary>
        /// Reads events for an aggregate from a specific version.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="fromVersion">The starting version.</param>
        /// <returns>Events from the specified version onwards.</returns>
        IEnumerable<IEvent> ReadFromVersion(Guid aggregateId, long fromVersion);

        /// <summary>
        /// Gets the current version of an aggregate.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <returns>The current version, or 0 if the aggregate doesn't exist.</returns>
        long GetVersion(Guid aggregateId);

        /// <summary>
        /// Reads all events from a specific point in time.
        /// </summary>
        /// <param name="from">The start time.</param>
        /// <returns>All events after the specified time.</returns>
        IEnumerable<IEvent> ReadAllFrom(DateTime from);
    }

    /// <summary>
    /// Async interface for event storage in event sourcing.
    /// </summary>
    public interface IAsyncEventStore
    {
        /// <summary>
        /// Appends a new event to the event store.
        /// </summary>
        /// <param name="event">The event to append.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task AppendAsync(IEvent @event, CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends multiple events atomically.
        /// </summary>
        /// <param name="events">The events to append.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task AppendRangeAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads all events for an aggregate.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All events for the aggregate, in order.</returns>
        Task<IEnumerable<IEvent>> ReadAsync(Guid aggregateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads events for an aggregate up to a specific version.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="maxVersion">The maximum version to read.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Events up to the specified version.</returns>
        Task<IEnumerable<IEvent>> ReadUpToVersionAsync(Guid aggregateId, long maxVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads events for an aggregate from a specific version.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="fromVersion">The starting version.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Events from the specified version onwards.</returns>
        Task<IEnumerable<IEvent>> ReadFromVersionAsync(Guid aggregateId, long fromVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current version of an aggregate.
        /// </summary>
        /// <param name="aggregateId">The aggregate ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The current version, or 0 if the aggregate doesn't exist.</returns>
        Task<long> GetVersionAsync(Guid aggregateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads all events from a specific point in time.
        /// </summary>
        /// <param name="from">The start time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All events after the specified time.</returns>
        Task<IEnumerable<IEvent>> ReadAllFromAsync(DateTime from, CancellationToken cancellationToken = default);
    }
}

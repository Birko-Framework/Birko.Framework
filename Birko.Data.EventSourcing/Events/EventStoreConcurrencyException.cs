using System;

namespace Birko.Data.EventSourcing.Events
{
    /// <summary>
    /// An append was refused because the aggregate already has an event at that version — another writer
    /// appended first. Reload the aggregate and retry the command against its current version.
    /// </summary>
    /// <remarks>
    /// TASK-529. The contract's optimistic-concurrency signal, so a caller can catch one type whatever backs
    /// the event store. Nothing is written when it is thrown: an append-range that hits it is rolled back as a
    /// whole.
    /// </remarks>
    public class EventStoreConcurrencyException : Exception
    {
        /// <summary>The aggregate the conflicting append targeted.</summary>
        public Guid AggregateGuid { get; }

        /// <summary>The version that was already taken.</summary>
        public long Version { get; }

        public EventStoreConcurrencyException(Guid aggregateGuid, long version, Exception? innerException = null)
            : base($"Aggregate {aggregateGuid} already has an event at version {version} — another writer appended first. "
                + "Reload the aggregate and retry against its current version.", innerException)
        {
            AggregateGuid = aggregateGuid;
            Version = version;
        }
    }
}

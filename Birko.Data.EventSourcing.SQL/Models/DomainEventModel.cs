using System;
using Birko.Data.EventSourcing.Events;
using Birko.Data.Models;

namespace Birko.Data.EventSourcing.SQL.Models
{
    /// <summary>
    /// One row per domain event, in the <c>__Events</c> table. The event's own <see cref="IEvent.EventGuid"/> is a
    /// column of its own: the store assigns the row's <c>Guid</c> on create, so it cannot carry the event's id.
    /// </summary>
    /// <remarks>
    /// <b>(AggregateGuid, Version) is UNIQUE</b>, and that index is the concurrency guard: two writers appending the
    /// same version of one aggregate — in two processes, on two machines — cannot both commit. A process-local lock
    /// (what DraCode's store relied on) guards nothing once a second instance runs.
    /// </remarks>
    [Birko.Data.SQL.Attributes.Table("__Events")]
    [Birko.Data.SQL.Attributes.CompositeIndex(UniqueIndexName, nameof(AggregateGuid), nameof(Version), IsUnique = true)]
    public class DomainEventModel : AbstractModel
    {
        /// <summary>The unique (aggregate, version) index — ensured explicitly by <c>SqlEventStore</c>.</summary>
        public const string UniqueIndexName = "UX___Events_AggregateGuid_Version";

        [Birko.Data.SQL.Attributes.PrimaryField]
        [Birko.Data.SQL.Attributes.NamedField("Guid")]
        public override Guid? Guid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("EventGuid")]
        public Guid EventGuid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("AggregateGuid")]
        public Guid AggregateGuid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("Version")]
        public long Version { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("EventType")]
        public string EventType { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("OccurredAt")]
        [Birko.Data.SQL.Attributes.UtcField]
        [Birko.Data.SQL.Attributes.IndexedField("IX___Events_OccurredAt")]
        public DateTime OccurredAt { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("EventData")]
        public string EventData { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("Metadata")]
        public string? Metadata { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("UserGuid")]
        public Guid? UserGuid { get; set; }

        public static DomainEventModel FromEvent(IEvent e) => new()
        {
            EventGuid = e.EventGuid,
            AggregateGuid = e.AggregateGuid,
            Version = e.Version,
            EventType = e.EventType,
            OccurredAt = e.OccurredAt,
            EventData = e.EventData,
            Metadata = e.Metadata,
            UserGuid = e.UserGuid,
        };

        public DomainEvent ToEvent() => new()
        {
            EventGuid = EventGuid,
            AggregateGuid = AggregateGuid,
            Version = Version,
            EventType = EventType,
            OccurredAt = OccurredAt,
            EventData = EventData,
            Metadata = Metadata,
            UserGuid = UserGuid,
        };
    }
}

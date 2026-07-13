using System;
using Birko.Data.EventSourcing.Events;
using Birko.EventBus.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.EventSourcing
{
    /// <summary>
    /// CR-M186: DomainEventPublished copied AggregateId/Version/type/data/metadata/UserId from the
    /// source domain event but NOT its OccurredAt or EventId — so EventBase stamped the wrapper with
    /// the current time and a fresh Guid, defeating time-ordered and dedup-by-EventId replay. The
    /// wrapper must carry the source event's original timestamp and identity.
    /// </summary>
    public class DomainEventPublishedTests
    {
        [Fact]
        public void Constructor_PreservesSourceOccurredAtAndEventId()
        {
            var originalTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var originalId = Guid.NewGuid();
            var aggregateId = Guid.NewGuid();

            var domainEvent = new DomainEvent(aggregateId, 7, "Updated", "{\"x\":1}")
            {
                OccurredAt = originalTime,
                EventId = originalId,
                Metadata = "{\"m\":true}",
                UserId = Guid.NewGuid(),
            };

            var wrapper = new DomainEventPublished(domainEvent);

            wrapper.OccurredAt.Should().Be(originalTime, "the historical event time must survive wrapping (CR-M186)");
            wrapper.EventId.Should().Be(originalId, "the source event identity must be carried for dedup-by-EventId (CR-M186)");
            wrapper.AggregateId.Should().Be(aggregateId);
            wrapper.Version.Should().Be(7);
            wrapper.DomainEventType.Should().Be("Updated");
            wrapper.EventData.Should().Be("{\"x\":1}");
            wrapper.Metadata.Should().Be("{\"m\":true}");
            wrapper.UserId.Should().Be(domainEvent.UserId);
        }
    }
}

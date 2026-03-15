using System;
using Birko.EventBus.MessageQueue;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.MessageQueue
{
    public class EventEnvelopeTests
    {
        [Fact]
        public void EventEnvelope_DefaultHeaders_Empty()
        {
            var envelope = new EventEnvelope();

            envelope.Headers.Should().NotBeNull();
            envelope.Headers.Should().BeEmpty();
        }

        [Fact]
        public void EventEnvelope_PropertiesRoundTrip()
        {
            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var tenantId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var envelope = new EventEnvelope
            {
                EventId = eventId,
                EventType = "TestType",
                Source = "test",
                OccurredAt = now,
                CorrelationId = correlationId,
                TenantId = tenantId,
                Payload = "{\"value\":1}",
                Headers = new() { ["key"] = "value" }
            };

            envelope.EventId.Should().Be(eventId);
            envelope.EventType.Should().Be("TestType");
            envelope.Source.Should().Be("test");
            envelope.OccurredAt.Should().Be(now);
            envelope.CorrelationId.Should().Be(correlationId);
            envelope.TenantId.Should().Be(tenantId);
            envelope.Payload.Should().Be("{\"value\":1}");
            envelope.Headers["key"].Should().Be("value");
        }
    }
}

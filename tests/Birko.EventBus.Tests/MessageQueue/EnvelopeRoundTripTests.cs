using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.MessageQueue;
using Birko.EventBus.Tests.TestResources;
using Birko.MessageQueue;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.MessageQueue
{
    public class EnvelopeRoundTripTests
    {
        [Fact]
        public void EventEnvelope_SerializeDeserialize_RoundTrips()
        {
            var serializer = new JsonMessageSerializer();
            var evt = new OrderPlaced(Guid.NewGuid(), 42m);

            var envelope = new EventEnvelope
            {
                EventId = evt.EventId,
                EventType = evt.GetType().AssemblyQualifiedName!,
                Source = evt.Source,
                OccurredAt = evt.OccurredAt,
                Payload = serializer.Serialize(evt)
            };

            var json = serializer.Serialize(envelope);
            var deserialized = serializer.Deserialize<EventEnvelope>(json);

            deserialized.Should().NotBeNull();
            deserialized!.EventId.Should().Be(evt.EventId);
            deserialized.EventType.Should().Contain("OrderPlaced");
        }

        [Fact]
        public void TypeGetType_ResolvesEventType()
        {
            var typeName = typeof(OrderPlaced).AssemblyQualifiedName!;
            var resolved = Type.GetType(typeName);

            resolved.Should().NotBeNull();
            resolved.Should().Be(typeof(OrderPlaced));
        }

        [Fact]
        public void EventPayload_DeserializesCorrectly()
        {
            var serializer = new JsonMessageSerializer();
            var evt = new OrderPlaced(Guid.NewGuid(), 42m);

            var payload = serializer.Serialize(evt);
            var deserialized = serializer.Deserialize(payload, typeof(OrderPlaced)) as OrderPlaced;

            deserialized.Should().NotBeNull();
            deserialized!.OrderId.Should().Be(evt.OrderId);
            deserialized.Total.Should().Be(42m);
        }

        [Fact]
        public async Task FullConsumerFlow_SimulatedMessage_Dispatches()
        {
            // Simulate what the consumer callback does manually
            var serializer = new JsonMessageSerializer();
            var evt = new OrderPlaced(Guid.NewGuid(), 42m);

            // Step 1: Build envelope (what PublishAsync does)
            var envelope = new EventEnvelope
            {
                EventId = evt.EventId,
                EventType = evt.GetType().AssemblyQualifiedName!,
                Source = evt.Source,
                OccurredAt = evt.OccurredAt,
                Payload = serializer.Serialize(evt)
            };

            var body = serializer.Serialize(envelope);

            // Step 2: Simulate receiving QueueMessage
            var message = new QueueMessage { Body = body };

            // Step 3: Deserialize envelope (what SubscribeToTransportAsync callback does)
            var receivedEnvelope = serializer.Deserialize<EventEnvelope>(message.Body);
            receivedEnvelope.Should().NotBeNull();

            var eventType = Type.GetType(receivedEnvelope!.EventType);
            eventType.Should().NotBeNull();
            typeof(OrderPlaced).IsAssignableFrom(eventType).Should().BeTrue();

            var deserializedEvent = serializer.Deserialize(receivedEnvelope.Payload, eventType!) as OrderPlaced;
            deserializedEvent.Should().NotBeNull();
            deserializedEvent!.OrderId.Should().Be(evt.OrderId);

            // Step 4: Dispatch to handler
            var handler = new OrderPlacedHandler();
            var context = new EventContext
            {
                EventId = receivedEnvelope.EventId,
                Source = receivedEnvelope.Source
            };

            await handler.HandleAsync(deserializedEvent, context);

            handler.ReceivedEvents.Should().ContainSingle()
                .Which.OrderId.Should().Be(evt.OrderId);
        }
    }
}

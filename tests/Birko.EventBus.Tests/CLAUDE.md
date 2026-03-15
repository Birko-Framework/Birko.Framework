# Birko.EventBus.Tests

## Overview
Unit tests for all Birko.EventBus projects — core event bus, pipeline, deduplication, topic conventions, distributed bus, outbox, and event sourcing integration.

## Project Location
- **Directory:** `C:\Source\Birko.EventBus.Tests\`
- **Type:** Test Project (.csproj, xUnit)
- **Target:** net10.0

## Components

| File | Description |
|------|-------------|
| **Core tests** | |
| InProcessEventBusTests.cs | Publish/subscribe, multi-handler, error isolation, parallel dispatch, dispose |
| EventPipelineTests.cs | Pipeline behavior ordering (Russian doll pattern) |
| DeduplicationTests.cs | InMemoryDeduplicationStore + DeduplicationBehavior |
| TopicConventionTests.cs | DefaultTopicConvention + AttributeTopicConvention |
| EventBaseTests.cs | EventBase record properties, EventContext.From() |
| DiRegistrationTests.cs | AddEventBus(), AddEventHandler, options configuration |
| **MessageQueue tests** | |
| MessageQueue/DistributedEventBusTests.cs | Publish to queue, receive & dispatch, context propagation, topic routing |
| MessageQueue/EventEnvelopeTests.cs | Envelope property round-trip |
| MessageQueue/EnvelopeRoundTripTests.cs | Serialize/deserialize, Type.GetType, full consumer flow |
| **Outbox tests** | |
| Outbox/InMemoryOutboxStoreTests.cs | Save, get pending, batch size, mark published/failed, cleanup |
| Outbox/OutboxEventBusTests.cs | Writes to store, doesn't publish to inner, serializes payload |
| Outbox/OutboxProcessorTests.cs | Publish pending, mark published, multiple, invalid type, cleanup, end-to-end |
| **EventSourcing tests** | |
| EventSourcing/EventStoreEventBusTests.cs | Append publishes, persists to inner, append range, read/version delegation |
| EventSourcing/EventReplayServiceTests.cs | Replay aggregate, from version, from timestamp, empty |
| EventSourcing/TestAsyncEventStore.cs | In-memory IAsyncEventStore for testing |
| **Test resources** | |
| TestResources/TestEvents.cs | OrderPlaced, OrderCancelled, DeviceOffline test events |
| TestResources/TestHandlers.cs | OrderPlacedHandler, ThrowingHandler, ContextCapturingHandler |

## Dependencies
- Birko.EventBus (projitems)
- Birko.EventBus.MessageQueue (projitems)
- Birko.EventBus.Outbox (projitems)
- Birko.EventBus.EventSourcing (projitems)
- Birko.MessageQueue + InMemory (projitems)
- Birko.Data.Core + Birko.Data.Stores + Models (projitems)
- Birko.Data.EventSourcing (projitems)
- xUnit 2.9.3, FluentAssertions 7.0.0
- Microsoft.Extensions.DependencyInjection, Hosting.Abstractions

## Maintenance
- Add tests for any new event bus features
- Follow existing test patterns (xUnit + FluentAssertions)

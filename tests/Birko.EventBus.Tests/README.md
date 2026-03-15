# Birko.EventBus.Tests

Unit tests for all Birko.EventBus projects — core event bus, pipeline, deduplication, distributed bus, outbox, and event sourcing integration.

## Test Coverage

- **Core** (34 tests) — InProcessEventBus, EventPipeline, Deduplication, TopicConventions, EventBase, DI registration
- **MessageQueue** (14 tests) — DistributedEventBus publish/subscribe, EventEnvelope serialization, round-trip
- **Outbox** (18 tests) — InMemoryOutboxStore, OutboxEventBus decorator, OutboxProcessor end-to-end
- **EventSourcing** (9 tests) — EventStoreEventBus decorator, EventReplayService

**Total: 75 tests**

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **.NET 10.0**

## Running Tests

```bash
dotnet test
```

## License

[MIT](License.md)

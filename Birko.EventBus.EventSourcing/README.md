# Birko.EventBus.EventSourcing

Integration between Birko.EventBus and Birko.Data.EventSourcing. Automatically publishes domain events to the event bus when they are appended to the event store, and provides replay capabilities for rebuilding projections.

## Features

- **Automatic publishing** — `EventStoreEventBus` decorator publishes `DomainEventPublished` after each append
- **Event replay** — `EventReplayService` replays historical events through the bus for projection rebuilding
- **Replay by aggregate** — Replay all events or from a specific version
- **Replay by timestamp** — Replay all events from a point in time

## Usage

### Automatic event publishing

```csharp
// Wrap your async event store with the decorator
var innerStore = new MyAsyncEventStore();
var eventBus = serviceProvider.GetRequiredService<IEventBus>();
var store = new EventStoreEventBus(innerStore, eventBus);

// When you append, it's persisted AND published to the bus
await store.AppendAsync(new DomainEvent(aggregateId, 1, "Created", payload));
```

### Handle published domain events

```csharp
public class OrderProjectionHandler : IEventHandler<DomainEventPublished>
{
    public Task HandleAsync(DomainEventPublished @event, EventContext context, CancellationToken ct)
    {
        // Update read model based on domain event
        if (@event.DomainEventType == "Created") { /* ... */ }
        return Task.CompletedTask;
    }
}
```

### Replay events for projection rebuilding

```csharp
var replayService = new EventReplayService(eventStore, eventBus);

// Replay all events for an aggregate
await replayService.ReplayAggregateAsync(aggregateId);

// Replay from a specific version
await replayService.ReplayFromVersionAsync(aggregateId, fromVersion: 5);

// Replay all events from a timestamp
await replayService.ReplayAllFromAsync(DateTime.UtcNow.AddDays(-1));
```

## Dependencies

- **Birko.EventBus** — IEventBus, EventBase, IEventHandler
- **Birko.Data.EventSourcing** — IAsyncEventStore, IEvent

## License

[MIT](License.md)

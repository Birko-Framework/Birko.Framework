# Birko.Data.EventSourcing

Decorator-style event sourcing for Birko stores. Wraps any `IStore<T>` / `IAsyncStore<T>` (including
the bulk variants) so every Create / Update / Delete also appends a `DomainEvent` to an `IEventStore`.
Reads pass through to the inner store. Replay APIs rebuild aggregate state from the event stream.

This project ships **interfaces, wrappers, and an aggregate base class only** — no concrete
`IEventStore` backend, no snapshot store, no built-in projections. Bring your own event-store
implementation (typically backed by another Birko store, e.g. SQL / Mongo / JSON).

## Features

- Record every state change as an immutable `DomainEvent` alongside the normal write
- Rebuild (`Replay`) an aggregate's state by folding its event stream
- Full event history per aggregate (`GetHistory`)
- Optional `CurrentUserId` stamping on emitted events
- Composes into the `StoreWrapperBuilder` decorator chain (innermost)

## Dependencies

- Birko.Data.Core (`AbstractModel`, filters)
- Birko.Data.Stores (`IStore<T>` / `IAsyncStore<T>` / bulk variants, `IStoreWrapper<T>`)
- Birko.Configuration
- Birko.Serialization (`ISerializer`; defaults to `SystemJsonSerializer`)
- Birko.Time (`IDateTimeProvider`; defaults to `SystemDateTimeProvider`)

## Usage

```csharp
using Birko.Data.EventSourcing.Stores;   // .WithEventSourcing, wrapper types
using Birko.Data.EventSourcing.Models;    // IEventSourced, EventSourcedAggregate

// 1. The entity implements IEventSourced (or extends EventSourcedAggregate).
public class Customer : AbstractModel, IEventSourced
{
    public long Version { get; set; }
    public string Name { get; set; } = string.Empty;
    // implement ApplyEvent / GetUncommittedEvents / MarkEventsAsCommitted / LoadFromEvents,
    // or inherit EventSourcedAggregate.
}

// 2. Wrap a raw store with your own IAsyncEventStore backend.
IAsyncEventStore eventStore = /* your backend (adapt a Birko store) */;
IAsyncBulkStore<Customer> raw = new MongoStore<Customer>(/* … */);

IAsyncBulkStore<Customer> store = raw.WithEventSourcing(eventStore); // same interface back

// 3. Writes append an event, then delegate to the inner store.
await store.CreateAsync(new Customer { Name = "Acme" });
// → eventStore.AppendAsync(DomainEvent("Created", …)) then raw.CreateAsync(…)

// 4. Replay / history (cast to the wrapper type).
var wrapper = (AsyncEventSourcingBulkStoreWrapper<IAsyncBulkStore<Customer>, Customer>)store;
Customer rebuilt = await wrapper.ReplayAsync(aggregateId);
var history = await wrapper.GetHistoryAsync(aggregateId);
```

## API Reference

### Events (`Events/`)
- **`IEvent`** — domain-event contract: `EventId`, `AggregateId`, `Version`, `EventType`
  (`"Created"` / `"Updated"` / `"Deleted"`), `OccurredAt`, `EventData` (serialized entity), `Metadata?`, `UserId?`.
- **`DomainEvent`** — default `IEvent` implementation.
- **`IEventStore` / `IAsyncEventStore`** — the backend contract the wrappers consume:
  `Append`/`AppendRange`, `Read(aggregateId)`, `ReadUpToVersion` / `ReadFromVersion`, `GetVersion`,
  `ReadAllFrom(DateTime)` (async variants take a `CancellationToken`). No concrete implementation ships here.

### Models (`Models/`)
- **`IEventSourced`** — `Version`, `ApplyEvent`, `GetUncommittedEvents`, `MarkEventsAsCommitted`, `LoadFromEvents`.
- **`EventSourcedAggregate`** — default base class buffering uncommitted events.

### Stores (`Stores/`)
- **`EventSourcingStoreWrapper<TStore,T>`** / **`AsyncEventSourcingStoreWrapper<TStore,T>`**
- **`EventSourcingBulkStoreWrapper<TStore,T>`** / **`AsyncEventSourcingBulkStoreWrapper<TStore,T>`**
  (each also implements `IStoreWrapper<T>`; constraint `T : AbstractModel, IEventSourced`;
  ctor `(innerStore, eventStore, ISerializer? = null, IDateTimeProvider? = null)`; expose `Replay`/`GetHistory`).
- **`.WithEventSourcing(eventStore, …)`** — extension per store interface; returns the same interface.

## Filter-Based Bulk Operations

The bulk wrappers record events for filter-based bulk operations by reading the matching entities and
raising per-entity events (no native UpdateByQuery / filter-delete optimization):
- `Update(filter, PropertyUpdate<T>)` — read-modify-save, one `Updated` event per entity
- `Update(filter, Action<T>)` — read-modify-save with event recording
- `Delete(filter)` — read-then-delete, one `Deleted` event per entity

## Notes

- Events are appended **before** the inner write; atomicity (transaction / outbox) is the event-store
  backend's responsibility.
- Only `"Created"` / `"Updated"` / `"Deleted"` are emitted; custom event types require raising via
  `eventStore.Append` directly or a custom wrapper.
- Not included: a concrete `IEventStore`, a snapshot store, and projections. See `CLAUDE.md` for the
  full component/architecture notes and `Birko.EventBus.EventSourcing` for replay/event-bus glue.

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) — models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) — store interfaces
- [Birko.Data.Composition](../Birko.Data.Composition/) — `StoreWrapperBuilder` decorator chain

## License

Part of the Birko Framework.

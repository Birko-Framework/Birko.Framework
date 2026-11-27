# Birko.Data.EventSourcing

Decorator-style event sourcing for Birko stores. Wraps any `IStore<T>` / `IAsyncStore<T>` (including
the bulk variants) so every Create / Update / Delete also appends a `DomainEvent` to an `IEventStore`.
Reads pass through to the inner store. Replay APIs rebuild aggregate state from the event stream.

It also ships the **event store** itself: one `IAsyncEventStore` for whichever backend your `IStoreFactory` is configured
for, with native versions per backend in the `Birko.Data.EventSourcing.*` projects. It has no snapshot store and no
built-in projections.

## Features

- Record every state change as an immutable `DomainEvent` alongside the normal write
- Rebuild (`Replay`) an aggregate's state by folding its event stream
- Full event history per aggregate (`GetHistory`)
- Optional `currentUser` delegate, read at each write, stamping `UserGuid` on emitted events
- Composes into the `StoreWrapperBuilder` decorator chain (innermost)

## Dependencies

- Birko.Data.Core (`AbstractModel`, filters)
- Birko.Data.Stores (`IStore<T>` / `IAsyncStore<T>` / bulk variants, `IStoreWrapper<T>`)
- Birko.Configuration
- Birko.Serialization (`ISerializer`; defaults to `SystemJsonSerializer`)
- Birko.Time (`IDateTimeProvider`; defaults to `SystemDateTimeProvider`)

## The event store

```csharp
var factory = StoreFactory.Create(config["Store:Backend"], config["Store:ConnectionString"], p => p
    .AddSqLite().AddPostgreSQL().AddMongoDB());

IAsyncEventStore events = factory.GetEventStore(o => o.AddSql().AddMongoDB());

var next = await events.GetVersionAsync(aggregateId) + 1;
try
{
    await events.AppendAsync(new DomainEvent(aggregateId, next, "Renamed", json));
}
catch (EventStoreConcurrencyException)
{
    // another writer took this version first, in this or another process: reload and retry
}
```

| Backend | Event store | A batch of events |
|---|---|---|
| SQLite, PostgreSQL, MySQL, SQL Server | `SqlEventStore<DB>` (Birko.Data.EventSourcing.SQL, `o.AddSql()`) | one transaction |
| MongoDB | `MongoDBEventStore` (Birko.Data.EventSourcing.MongoDB, `o.AddMongoDB()`) | one transaction on a replica set or sharded cluster; the default store on a standalone server |
| RavenDB | `RavenDBEventStore` (Birko.Data.EventSourcing.RavenDB, `o.AddRavenDB()`) | one session |
| Cosmos DB | `CosmosDBEventStore` (Birko.Data.EventSourcing.CosmosDB, `o.AddCosmosDB()`) | one transactional batch, one aggregate per batch |
| Elasticsearch | `ElasticSearchEventStore` (Birko.Data.EventSourcing.ElasticSearch, `o.AddElasticSearch()`) | the default store, plus an index refresh per append |
| InMemory (tests) | the default `AsyncStoreEventStore` | the default store |
| JSON, XML | refused: they cannot turn away a write from a second process | — |

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
- **`IEvent`** — domain-event contract: `EventGuid`, `AggregateGuid`, `Version`, `EventType`
  (`"Created"` / `"Updated"` / `"Deleted"`), `OccurredAt`, `EventData` (serialized entity), `Metadata?`, `UserGuid?`.
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
- Not included: a sync event store implementation, a snapshot store, and projections. See `CLAUDE.md` for the
  full component/architecture notes and `Birko.EventBus.EventSourcing` for replay/event-bus glue.

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) — models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) — store interfaces
- [Birko.Data.Composition](../Birko.Data.Composition/) — `StoreWrapperBuilder` decorator chain

## License

Part of the Birko Framework.

# Birko.Data.EventSourcing

## Overview
Decorator-style event sourcing for Birko stores. Wraps any `IStore<T>` / `IAsyncStore<T>` (incl. bulk variants) so every Create / Update / Delete also appends a `DomainEvent` to an `IEventStore`. Reads pass through to the inner store. Provides Replay + history APIs to rebuild aggregate state from the event stream.

This project ships **interfaces, wrappers, and an aggregate base class only** — no concrete `IEventStore` backend, no snapshot store, no built-in projections. Bring your own event-store implementation (typically backed by another Birko store, e.g. SQL/Mongo/JSON).

## Project Location
`Birko.Data.EventSourcing/`

## Layout

```
Events/
  IEvent.cs                — domain event contract
  DomainEvent.cs           — default IEvent implementation
  IEventStore.cs           — IEventStore + IAsyncEventStore (append + read by aggregate/version/time)
Models/
  IEventSourced.cs         — IEventSourced contract + EventSourcedAggregate base class
Stores/
  EventSourcingStoreWrapper.cs           — sync IStore<T> wrapper
  AsyncEventSourcingStoreWrapper.cs      — async IAsyncStore<T> wrapper
  EventSourcingBulkStoreWrapper.cs       — sync IBulkStore<T> wrapper (extends sync wrapper)
  AsyncEventSourcingBulkStoreWrapper.cs  — async IAsyncBulkStore<T> wrapper
  EventSourcingStoreExtensions.cs        — .WithEventSourcing(...) extension methods
  EventSourcingRepositoryExtensions.cs   — EXCLUDED from compilation (depends on Birko.Data.ViewModel);
                                           consumers needing event-sourced ViewModel repos include it explicitly
```

## Components

### `IEvent` / `DomainEvent` (`Events/`)
`IEvent` fields: `EventId`, `AggregateId`, `Version`, `EventType` (string — `"Created"` / `"Updated"` / `"Deleted"` by default), `OccurredAt`, `EventData` (serialized entity, JSON), `Metadata?`, `UserId?`.

`DomainEvent` is the default `IEvent` implementation. Two constructors: parameterless (auto-assigns `EventId` + `OccurredAt`) and a fully-specified one used by the wrappers.

### `IEventStore` / `IAsyncEventStore` (`Events/IEventStore.cs`)
The event-store contract consumed by the wrappers. Members:
- `Append(IEvent)` / `AppendRange(IEnumerable<IEvent>)`
- `Read(Guid aggregateId)` — all events for an aggregate, in order
- `ReadUpToVersion(Guid, long)` / `ReadFromVersion(Guid, long)` — version-range slices
- `GetVersion(Guid)` — current aggregate version (0 if none)
- `ReadAllFrom(DateTime)` — global tail-read by time

Async variants take a `CancellationToken`. No concrete implementation lives here.

### `IEventSourced` / `EventSourcedAggregate` (`Models/IEventSourced.cs`)
Entity contract for aggregates the wrappers can sit in front of:
- `long Version { get; set; }` — optimistic-concurrency token
- `ApplyEvent(IEvent)` — fold an event into the aggregate
- `GetUncommittedEvents()` / `MarkEventsAsCommitted()` — uncommitted-event buffer
- `LoadFromEvents(IEnumerable<IEvent>)` — replay helper

`EventSourcedAggregate` is a default base class that tracks uncommitted events in a `List<IEvent>`, sets `Version` from the applied event, and clears the buffer on commit.

### Wrappers (`Stores/`)
All four implement the same store interface as the inner store **and** `IStoreWrapper<T>`. Generic constraint: `T : AbstractModel, IEventSourced`. Constructor takes `(innerStore, eventStore, ISerializer? serializer = null, IDateTimeProvider? clock = null)`. Defaults: `SystemJsonSerializer`, `SystemDateTimeProvider`.

Write-side behavior is uniform across the four:
1. Compute `newVersion = eventStore.GetVersion(aggregateId) + 1`
2. Build a `DomainEvent("Created" | "Updated" | "Deleted", serializer.Serialize(item), CurrentUserId, clock)`
3. **Append to event store first**, then delegate to the inner store
4. Set `item.Version = newVersion` on the entity before persisting

The bulk wrappers also implement the filter-based `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, and `Delete(filter)` overloads — they read the matching entities, raise per-entity events via `AppendRange`, then call the inner bulk store. (No native UpdateByQuery / filter-delete optimization — event recording requires per-entity events.)

Replay APIs (sync + async):
- `Replay(aggregateId)` — `CreateInstance()` + `IEventSourced.LoadFromEvents(events)`
- `GetHistory(aggregateId)` — raw event list for the aggregate

Optional `CurrentUserId` setter on the wrapper instance stamps `IEvent.UserId` on every emitted event.

### `.WithEventSourcing(...)` extensions (`Stores/EventSourcingStoreExtensions.cs`)
One overload per store interface (`IStore<T>`, `IAsyncStore<T>`, `IBulkStore<T>`, `IAsyncBulkStore<T>`). Returns the same interface. The `IStore<T>` / `IAsyncStore<T>` overloads upcast to the bulk wrapper automatically if the store is already bulk.

## Usage

```csharp
public class Customer : AbstractModel, IEventSourced
{
    public long Version { get; set; }
    public string Name { get; set; } = string.Empty;

    // … implement ApplyEvent / GetUncommittedEvents / etc.
    // or inherit EventSourcedAggregate
}

IAsyncEventStore eventStore = /* your backend */;
IAsyncBulkStore<Customer> raw = new MongoStore<Customer>(/* … */);

var store = raw.WithEventSourcing(eventStore);   // returns IAsyncBulkStore<Customer>

await store.CreateAsync(new Customer { Name = "Acme" });
// → eventStore.AppendAsync(DomainEvent("Created", …)) then raw.CreateAsync(…)

var rebuilt = await ((AsyncEventSourcingBulkStoreWrapper<IAsyncBulkStore<Customer>, Customer>)store)
    .ReplayAsync(aggregateId);
```

To set the user on emitted events, cast to the wrapper type and set `CurrentUserId`.

## Composition
`Birko.Data.Composition.StoreWrapperBuilder.Build<T>` wires event sourcing into the decorator chain automatically — pass an `IAsyncEventStore` and any `T : IEventSourced` gets the wrapper applied innermost:

```csharp
var store = StoreWrapperBuilder.Build(raw, clock, audit, tenant, eventStore);
```

Chain order: `Tenant → Default → Sluggable → SoftDelete → Audit → Timestamp → EventSourcing → RawStore`. EventSourcing is innermost so the event payload reflects Timestamp/Audit enrichments and outer-wrapper rejections (slug collision, default conflict, tenant guard) do not produce orphan events. Soft-deletes emit an `"Updated"` event with `IsDeleted=true` because SoftDelete converts the delete before EventSourcing sees it.

To layer event sourcing manually (e.g. outside the builder, or to wrap an `IAsyncStore<T>` that isn't bulk), use the extension:

```csharp
var store = raw.WithEventSourcing(eventStore, clock);
```

## Dependencies
- Birko.Data.Core — `AbstractModel`, filters, `ModelByGuid<T>`
- Birko.Data.Stores — `IStore<T>`, `IAsyncStore<T>`, `IBulkStore<T>`, `IAsyncBulkStore<T>`, `IStoreWrapper<T>`, `StoreDataDelegate<T>`, `PropertyUpdate<T>`, `OrderBy<T>`
- Birko.Configuration
- Birko.Serialization — `ISerializer`; defaults to `SystemJsonSerializer` from `Birko.Serialization.Json`
- Birko.Time — `IDateTimeProvider`; defaults to `SystemDateTimeProvider`

## Conventions
- Concrete entities must implement `IEventSourced` (or extend `EventSourcedAggregate`).
- `EventType` is a string — only `"Created"` / `"Updated"` / `"Deleted"` are emitted by the wrappers. Custom event types require a custom path (raise via `eventStore.Append` directly, or subclass a wrapper).
- Events are appended **before** the inner write — if the inner write fails, you may end up with an event for a row that doesn't exist. Atomicity is the responsibility of the event-store backend (transaction with the inner store, outbox, etc.).
- Bulk filter-based `Update(filter, …)` / `Delete(filter)` are read-modify-write internally so that per-aggregate events can be emitted. Do not expect native UpdateByQuery performance.

## Not Included (Plan If Needed)
- Concrete `IEventStore` implementation — pick a Birko store and adapt it.
- Snapshot store — not modeled here despite earlier docs mentioning it.
- Projections / read-model fan-out — see `Birko.EventBus.EventSourcing` for replay/event-bus glue (`DomainEventPublished`, `EventReplayService`, `EventStoreEventBus`).

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions

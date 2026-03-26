# Birko.Data.EventSourcing

Event sourcing implementation for the Birko Framework providing audit trails, temporal queries, and event replay.

## Features

- Store all state changes as immutable events
- Rebuild entity state by replaying events
- Complete audit history for any entity
- Temporal queries (state at any point in time)
- Snapshot support for performance optimization
- Projections for building read models

## Installation

```bash
dotnet add package Birko.Data.EventSourcing
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- System.Text.Json

## Usage

```csharp
using Birko.Data.EventSourcing;

// Define events
public class CustomerCreatedEvent : CreatedEvent<Customer>
{
    public string Name { get; set; }
    public string Email { get; set; }
}

// Use event-sourced repository
var repo = new EventSourcedRepository<Customer>(eventStore);
var id = repo.Create(customer);
var history = eventStore.GetEvents(id); // Full audit trail
var pastState = repo.GetAtTime(id, someDate); // Temporal query
```

## API Reference

### Stores

- **EventStore\<T\>** / **AsyncEventStore\<T\>** - Event storage

### Repositories

- **EventSourcedRepository\<T\>** / **AsyncEventSourcedRepository\<T\>**

### Models

- **Event** - Base event (Id, EntityId, EventType, Timestamp, Version, Data)
- **EventStream** - Collection of events for an entity
- **EventSnapshot** - Periodic state snapshot

### Built-in Events

- **CreatedEvent\<T\>** / **UpdatedEvent\<T\>** / **DeletedEvent\<T\>**

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces

## Filter-Based Bulk Operations

Event sourcing wrappers record events for all filter-based bulk operations:
- `Update(filter, PropertyUpdate<T>)` — Falls back to read-modify-save to record individual Updated events per entity
- `Delete(filter)` — Falls back to read-then-delete to record individual Deleted events per entity
- `Update(filter, Action<T>)` — Read-modify-save with event recording

## License

Part of the Birko Framework.

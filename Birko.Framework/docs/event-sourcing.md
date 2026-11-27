# Event Sourcing Guide

## Overview

Birko.Data.EventSourcing implements the event sourcing pattern where state changes are stored as a sequence of immutable events. Instead of storing current state, the system stores every change and reconstructs state by replaying events.

## Core Components

### Event

```csharp
public class Event
{
    public Guid Id { get; }
    public Guid EntityGuid { get; }     // Aggregate ID
    public string EventType { get; }   // Discriminator
    public DateTime Timestamp { get; }
    public long Version { get; }       // Ordering within a stream
    public string Data { get; }        // Serialized event data
}
```

### Built-in Event Types

```csharp
public class CreatedEvent<T> : Event { }
public class UpdatedEvent<T> : Event { }
public class DeletedEvent<T> : Event { }
```

### EventStream

A collection of events for a single aggregate, ordered by version.

### EventSnapshot

Periodic state snapshot for performance — avoids replaying the entire event history.

## Event Store

`IAsyncEventStore` (Birko.Data.EventSourcing) appends events with per-aggregate versions and reads them back:
- an aggregate's history: all of it, up to a version, or from a version;
- its current version;
- every event from a point in time.

A second writer of a version that is already taken, in this process or another, gets
`EventStoreConcurrencyException`, and nothing from its batch is kept.

Get one for whichever backend is configured, without naming it in application code:

```csharp
var factory = StoreFactory.Create(backend, connectionString, p => p.AddSqLite().AddMongoDB().AddCosmosDB());
IAsyncEventStore events = factory.GetEventStore(o => o.AddSql().AddMongoDB().AddCosmosDB());
```

It follows the store pattern:
- `AbstractAsyncEventStore` is the contract.
- `AsyncStoreEventStore` is a default over any store. Its concurrency comes from a record id derived from aggregate and
  version.
- Each backend that does better natively has its own version:

| Backend | Event store | A batch of events |
|---|---|---|
| SQLite, PostgreSQL, MySQL, SQL Server | `SqlEventStore<DB>` (Birko.Data.EventSourcing.SQL, `o.AddSql()`) | one transaction |
| MongoDB | `MongoDBEventStore` (Birko.Data.EventSourcing.MongoDB, `o.AddMongoDB()`) | one transaction on a replica set or sharded cluster; the default store on a standalone server |
| RavenDB | `RavenDBEventStore` (Birko.Data.EventSourcing.RavenDB, `o.AddRavenDB()`) | one session |
| Cosmos DB | `CosmosDBEventStore` (Birko.Data.EventSourcing.CosmosDB, `o.AddCosmosDB()`) | one transactional batch, one aggregate per batch |
| Elasticsearch | `ElasticSearchEventStore` (Birko.Data.EventSourcing.ElasticSearch, `o.AddElasticSearch()`) | the default store, plus an index refresh per append |
| InMemory (tests) | the default `AsyncStoreEventStore` | the default store |
| JSON, XML | refused: they cannot turn away a write from a second process | — |

## Store wrappers

`.WithEventSourcing(eventStore)` wraps any Birko store, so each Create, Update and Delete also appends a `DomainEvent`
(`EventSourcingStoreWrapper` and its async and bulk variants). `Replay` and `GetHistory` rebuild an aggregate.

## Usage

### Define Domain Events

```csharp
public class CustomerCreatedEvent : CreatedEvent<Customer>
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class CustomerEmailChangedEvent : UpdatedEvent<Customer>
{
    public string OldEmail { get; set; }
    public string NewEmail { get; set; }
}
```

### Store Events

```csharp
var store = new AsyncEventStore<Customer>(underlyingStore);

var @event = new CustomerCreatedEvent
{
    EntityGuid = customerId,
    Name = "John Doe",
    Email = "john@example.com"
};

await store.AppendEventAsync(@event);
```

### Rebuild State from Events

```csharp
public Customer Read(Guid id)
{
    var events = eventStore.GetEvents(id);
    var customer = new Customer();
    foreach (var @event in events)
    {
        Apply(customer, @event);
    }
    return customer;
}

private void Apply(Customer customer, Event @event)
{
    switch (@event)
    {
        case CustomerCreatedEvent created:
            customer.Name = created.Name;
            customer.Email = created.Email;
            break;
        case CustomerEmailChangedEvent emailChanged:
            customer.Email = emailChanged.NewEmail;
            break;
    }
}
```

### Temporal Queries

Query state at any point in time:

```csharp
public Customer GetAtTime(Guid id, DateTime timestamp)
{
    var events = eventStore.GetEvents(id)
        .Where(e => e.Timestamp <= timestamp);
    return Rebuild(events);
}
```

## Best Practices

1. **Events are immutable** — never modify stored events
2. **Version events** — plan for schema evolution from the start
3. **Use snapshots** — for aggregates with long event histories, snapshot periodically
4. **Idempotent replay** — event application must produce the same state regardless of replay count
5. **Small events** — store only what changed, not the entire entity state

## See Also

- [Birko.Data.EventSourcing](https://github.com/Birko-Framework/Birko.Framework/tree/main/Birko.Data.EventSourcing)

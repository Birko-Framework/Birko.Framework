# Event Sourcing Guide

## Overview

Birko.Data.EventSourcing implements the event sourcing pattern where state changes are stored as a sequence of immutable events. Instead of storing current state, the system stores every change and reconstructs state by replaying events.

## Core Components

### Event

```csharp
public class Event
{
    public Guid Id { get; }
    public Guid EntityId { get; }     // Aggregate ID
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

```csharp
// Sync
public class EventStore<T> where T : AbstractModel
{
    IEnumerable<Event> GetEvents(Guid entityId);
    void AppendEvent(Event @event);
}

// Async
public class AsyncEventStore<T> where T : AbstractModel
{
    Task<IEnumerable<Event>> GetEventsAsync(Guid entityId, CancellationToken ct = default);
    Task AppendEventAsync(Event @event, CancellationToken ct = default);
}
```

## Event-Sourced Repository

```csharp
// Sync
public class EventSourcedRepository<T> where T : AbstractModel { }

// Async
public class AsyncEventSourcedRepository<T> where T : AbstractModel { }
```

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
    EntityId = customerId,
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

- [Birko.Data.EventSourcing CLAUDE.md](../Birko.Data.EventSourcing/CLAUDE.md)

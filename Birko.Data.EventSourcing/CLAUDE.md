# Birko.Data.EventSourcing

## Overview
Event sourcing implementation for the Birko data layer providing audit trails and event replay capabilities.

## Project Location
`C:\Source\Birko.Data.EventSourcing\`

## Purpose
- Store all changes as events
- Rebuild state from events
- Complete audit history
- Temporal queries

## Components

### Stores
- `EventStore<T>` - Event store
- `AsyncEventStore<T>` - Async event store

### Repositories
- `EventSourcedRepository<T>` - Event-sourced repository
- `AsyncEventSourcedRepository<T>` - Async event-sourced repository

### Models
- `Event` - Base event
- `EventStream` - Collection of events for an entity
- `EventSnapshot` - Periodic state snapshot

### Events
- `CreatedEvent<T>` - Entity created
- `UpdatedEvent<T>` - Entity updated
- `DeletedEvent<T>` - Entity deleted

## Event Model

```csharp
public abstract class Event
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string EventType { get; set; }
    public DateTime Timestamp { get; set; }
    public int Version { get; set; }
    public string Data { get; set; } // Serialized event data
}
```

## Creating Events

```csharp
using Birko.Data.EventSourcing.Events;

public class CustomerCreatedEvent : CreatedEvent<Customer>
{
    public string Name { get; set; }
    public string Email { get; set; }
}

public class CustomerEmailUpdatedEvent : Event
{
    public string OldEmail { get; set; }
    public string NewEmail { get; set; }
}
```

## Event Sourced Repository

```csharp
using Birko.Data.EventSourcing.Repositories;

public class CustomerRepository : EventSourcedRepository<Customer>
{
    public CustomerRepository(IEventStore<Event> eventStore) : base(eventStore)
    {
    }

    public override Guid Create(Customer entity)
    {
        var @event = new CustomerCreatedEvent
        {
            EntityId = Guid.NewGuid(),
            Name = entity.Name,
            Email = entity.Email
        };

        EventStore.Save(@event);
        return @event.EntityId;
    }

    public override Customer Read(Guid id)
    {
        var events = EventStore.GetEvents(id);
        return Rebuild(events);
    }

    private Customer Rebuild(IEnumerable<Event> events)
    {
        // Replay events to rebuild state
        var customer = new Customer();
        foreach (var @event in events)
        {
            Apply(customer, @event);
        }
        return customer;
    }
}
```

## Event Storage

```sql
CREATE TABLE events (
    id UUID PRIMARY KEY,
    entity_id UUID NOT NULL,
    event_type TEXT NOT NULL,
    data JSONB NOT NULL,
    timestamp TIMESTAMP NOT NULL,
    version INT NOT NULL,
    CONSTRAINT unique_event_version UNIQUE (entity_id, version)
);

CREATE INDEX idx_events_entity_id ON events(entity_id);
```

## Snapshots

For performance, create periodic snapshots:

```csharp
public class CustomerSnapshot
{
    public Guid EntityId { get; set; }
    public Customer State { get; set; }
    public int Version { get; set; }
    public DateTime Timestamp { get; set; }
}

// Load from snapshot then replay events since snapshot
public Customer Read(Guid id)
{
    var snapshot = SnapshotStore.GetLatest(id);
    var events = EventStore.GetEvents(id, fromVersion: snapshot.Version);
    return Rebuild(snapshot.State, events);
}
```

## Temporal Queries

Query entity state at any point in time:

```csharp
public Customer GetAtTime(Guid id, DateTime timestamp)
{
    var events = EventStore.GetEvents(id)
        .Where(e => e.Timestamp <= timestamp);

    return Rebuild(events);
}
```

## Audit Trail

Complete history of all changes:

```csharp
public IEnumerable<Event> GetAuditLog(Guid id)
{
    return EventStore.GetEvents(id);
}
```

## Projections

Build read models from events:

```csharp
public class CustomerSummaryProjection
{
    public void On(CustomerCreatedEvent @event)
    {
        // Update read model
    }

    public void On(CustomerEmailUpdatedEvent @event)
    {
        // Update read model
    }
}
```

## Dependencies
- Birko.Data.Core
- Birko.Data.Stores
- Birko.Serialization — ISerializer for event data serialization (optional, defaults to SystemJsonSerializer)
- System.Text.Json (or Newtonsoft.Json)

## Best Practices

1. **Event Versioning** - Use event version for compatibility
2. **Immutable Events** - Never modify stored events
3. **Snapshots** - Use snapshots for long event streams
4. **Idempotency** - Ensure events can be replayed safely
5. **Event Schema** - Design events carefully (they're permanent)

## Use Cases
- Financial systems (audit requirements)
- Version control
- Temporal data
- Audit logging
- CQRS read models
- Debugging (replay to find issues)

## Advantages
- Complete audit trail
- Temporal queries
- Easy debugging
- Event replay for recovery
- Natural fit for CQRS

## Challenges
- More complex than traditional CRUD
- Requires careful event design
- Event versioning required
- Snapshots needed for performance

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

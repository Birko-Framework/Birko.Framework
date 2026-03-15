# Event Bus Guide

## Overview

Birko.EventBus provides a publish-subscribe event bus with four composable layers:

| Layer | Project | Use Case |
|-------|---------|----------|
| **In-Process** | Birko.EventBus | Single process, local handlers |
| **Distributed** | Birko.EventBus.MessageQueue | Cross-process delivery via MessageQueue |
| **Outbox** | Birko.EventBus.Outbox | At-least-once delivery with transactional consistency |
| **Event Sourcing** | Birko.EventBus.EventSourcing | Bridge domain events from EventStore to EventBus |

## Core Concepts

### Events

All events implement `IEvent`:

```csharp
public interface IEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    string Source { get; }
}
```

Use `EventBase` as your base record:

```csharp
public sealed record OrderPlaced : EventBase
{
    public override string Source => "orders";
    public Guid OrderId { get; init; }
    public decimal Total { get; init; }
}
```

### Event Context

`EventContext` carries delivery metadata through the pipeline:

```csharp
public class EventContext
{
    public Guid EventId { get; init; }
    public string Source { get; init; }
    public Guid? CorrelationId { get; set; }
    public Guid? TenantId { get; set; }
    public int DeliveryCount { get; set; }
    public IDictionary<string, string> Metadata { get; set; }
}
```

### Handlers

```csharp
public class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct)
    {
        // React to event
        return Task.CompletedTask;
    }
}
```

### Event Bus Interface

```csharp
public interface IEventBus : IDisposable
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;
    IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
}
```

## In-Process Bus

The simplest setup — events are dispatched locally within the process.

### Registration

```csharp
services.AddEventBus(options =>
{
    options.MaxConcurrency = 1;              // Sequential dispatch (default)
    options.ErrorHandling = ErrorHandlingMode.Continue; // Isolate handler errors
});

services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
```

### Options

- **MaxConcurrency** — `1` for sequential, `> 1` for parallel dispatch with SemaphoreSlim
- **ErrorHandling** — `Continue` (catch, log, continue) or `Stop` (rethrow on first failure)

### Publishing

```csharp
public class OrderService(IEventBus eventBus)
{
    public async Task PlaceOrderAsync(Order order)
    {
        // ... save order ...
        await eventBus.PublishAsync(new OrderPlaced { OrderId = order.Id, Total = order.Total });
    }
}
```

## Pipeline Behaviors

Behaviors wrap event dispatch in a Russian-doll pattern (like middleware):

```csharp
public class LoggingBehavior : IEventPipelineBehavior
{
    public async Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken ct)
    {
        Console.WriteLine($"Before: {@event.GetType().Name}");
        await next();
        Console.WriteLine($"After: {@event.GetType().Name}");
    }
}

services.AddEventPipelineBehavior<LoggingBehavior>();
```

Behaviors execute in registration order (first registered = outermost).

## Enrichment

Enrichers populate `EventContext` before dispatch:

```csharp
// Built-in: ensures CorrelationId is set
services.AddEventEnricher<CorrelationEventEnricher>();
```

Custom enricher:

```csharp
public class TenantEnricher : IEventEnricher
{
    public Task EnrichAsync(IEvent @event, EventContext context, CancellationToken ct)
    {
        context.TenantId = TenantContext.CurrentTenantId;
        return Task.CompletedTask;
    }
}

services.AddEventEnricher<TenantEnricher>();
```

## Deduplication

Prevents duplicate event processing based on `EventId`:

```csharp
services.AddEventDeduplication(ttl: TimeSpan.FromHours(2));
```

Uses `InMemoryDeduplicationStore` by default. For distributed scenarios, implement `IDeduplicationStore` and register:

```csharp
services.AddEventDeduplication<RedisDeduplicationStore>();
```

## Topic Routing

Controls how event types map to message queue topics:

- **DefaultTopicConvention** — `OrderPlaced` → `"events.order-placed"` (kebab-case)
- **AttributeTopicConvention** — Uses `[Topic("custom-topic")]` attribute, falls back to default

```csharp
[Topic("orders.placed")]
public sealed record OrderPlaced : EventBase { ... }
```

## Distributed Bus

Delivers events across processes via `Birko.MessageQueue` transports (MQTT, InMemory, RabbitMQ, etc.).

### Registration

```csharp
// Requires IMessageQueue already registered (e.g., AddInMemoryMessageQueue or AddMqttMessageQueue)
services.AddDistributedEventBus(options =>
{
    options.TopicConvention = new AttributeTopicConvention();
    options.AutoSubscribe = true;  // Scan assemblies for handlers (default)
});

services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
```

### How It Works

**Publishing:**
1. Enrichers populate EventContext
2. Event wrapped in `EventEnvelope` (EventType, Payload JSON, Headers)
3. Sent to MessageQueue topic via `IMessageQueue.Producer.SendAsync`

**Consuming (AutoSubscribe):**
1. `DistributedEventBusHostedService` scans DI for all `IEventHandler<T>` implementations
2. Creates transport subscriptions for each event type
3. On message: deserialize envelope → reconstruct event → dispatch through pipeline → handlers

### EventEnvelope

The transport wrapper for distributed events:

```csharp
public class EventEnvelope
{
    public Guid EventId { get; set; }
    public string EventType { get; set; }    // AssemblyQualifiedName
    public string Payload { get; set; }       // JSON
    public Guid? CorrelationId { get; set; }
    public Guid? TenantId { get; set; }
    public Dictionary<string, string> Headers { get; set; }
}
```

## Transactional Outbox

Guarantees at-least-once delivery by persisting events in the same database transaction as business data.

### Registration

```csharp
services.AddDistributedEventBus();      // or AddEventBus()
services.AddInMemoryOutbox(options =>
{
    options.BatchSize = 100;
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.MaxAttempts = 5;
    options.RetentionPeriod = TimeSpan.FromDays(7);
});
services.AddOutboxEventBus();           // Decorates IEventBus with OutboxEventBus
```

### How It Works

1. **PublishAsync** (via `OutboxEventBus`) saves an `OutboxEntry` to the outbox store — same DB transaction as your business data
2. **OutboxProcessor** (background service) polls for pending entries, deserializes, and publishes via the inner bus
3. On success: entry marked `Published`. On failure: retried up to `MaxAttempts`, then marked `Failed`
4. Old entries cleaned up after `RetentionPeriod`

### Custom Outbox Store

Implement `IOutboxStore` for your database:

```csharp
public interface IOutboxStore
{
    Task SaveAsync(OutboxEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, CancellationToken ct = default);
    Task MarkPublishedAsync(Guid entryId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid entryId, string error, CancellationToken ct = default);
    Task CleanupAsync(DateTime cutoffDate, CancellationToken ct = default);
}

services.AddOutbox<SqlOutboxStore>();
```

## Event Sourcing Integration

Bridges `Birko.Data.EventSourcing` domain events to the event bus.

### EventStoreEventBus

Decorator for `IAsyncEventStore` that publishes `DomainEventPublished` after each append:

```csharp
var decoratedStore = new EventStoreEventBus(innerEventStore, eventBus);

// When you append a domain event...
await decoratedStore.AppendAsync(domainEvent);
// ...a DomainEventPublished event is automatically published to the event bus
```

### DomainEventPublished

```csharp
public sealed record DomainEventPublished : EventBase
{
    public Guid AggregateId { get; init; }
    public long Version { get; init; }
    public string DomainEventType { get; init; }
    public string EventData { get; init; }
}
```

### Event Replay

Replay historical domain events through the bus (e.g., to rebuild projections):

```csharp
var replayService = new EventReplayService(eventStore, eventBus);

// Replay all events for an aggregate
int count = await replayService.ReplayAggregateAsync(aggregateId);

// Replay from a specific version
int count = await replayService.ReplayFromVersionAsync(aggregateId, fromVersion: 5);

// Replay all events from a timestamp
int count = await replayService.ReplayAllFromAsync(DateTime.UtcNow.AddDays(-1));
```

## Composition Examples

**Basic in-process:**
```csharp
services.AddEventBus();
services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
```

**Distributed with dedup:**
```csharp
services.AddInMemoryMessageQueue();  // or MQTT, RabbitMQ, etc.
services.AddDistributedEventBus();
services.AddEventDeduplication();
services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
```

**Distributed with outbox + dedup + enrichment:**
```csharp
services.AddMqttMessageQueue(opts => { ... });
services.AddDistributedEventBus();
services.AddInMemoryOutbox();
services.AddOutboxEventBus();
services.AddEventDeduplication();
services.AddEventEnricher<CorrelationEventEnricher>();
services.AddEventEnricher<TenantEnricher>();
services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
```

## Dependencies

| Project | Dependencies |
|---------|-------------|
| Birko.EventBus | Microsoft.Extensions.DependencyInjection.Abstractions |
| Birko.EventBus.MessageQueue | Birko.EventBus, Birko.MessageQueue, MS.Extensions.Hosting.Abstractions |
| Birko.EventBus.Outbox | Birko.EventBus, Birko.MessageQueue (serializer), MS.Extensions.Hosting.Abstractions |
| Birko.EventBus.EventSourcing | Birko.EventBus, Birko.Data.EventSourcing |

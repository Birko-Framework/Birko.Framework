# Birko.EventBus

Strongly-typed event bus for the Birko Framework. Provides in-process pub/sub with pipeline behaviors, enrichment, deduplication, and DI integration. Designed for modular monolith architectures with an upgrade path to distributed messaging via Birko.MessageQueue.

## Features

- **Strongly-typed events** — `IEvent` marker interface, `EventBase` record with Id/Timestamp/CorrelationId/Source
- **In-process event bus** — `InProcessEventBus` with DI handler resolution and manual subscriptions
- **Pipeline behaviors** — Middleware chain (logging, retry, validation, dedup) via `IEventPipelineBehavior`
- **Event enrichment** — Pre-publish enrichers for CorrelationId, TenantGuid, custom headers
- **Deduplication** — `IDeduplicationStore` with in-memory TTL implementation
- **Topic conventions** — `ITopicConvention` for event type → topic name mapping (default kebab-case or attribute-based)
- **Error isolation** — Handler failures don't cascade to other handlers
- **DI integration** — `AddEventBus()`, `AddEventHandler<T,H>()`, `AddEventPipelineBehavior<T>()`

## Usage

### Define events

```csharp
public sealed record OrderPlaced(Guid OrderId, decimal Total) : EventBase
{
    public override string Source => "orders";
}
```

### Define handlers

```csharp
public class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct = default)
    {
        Console.WriteLine($"Order {@event.OrderId} placed for {@event.Total}");
        return Task.CompletedTask;
    }
}
```

### Register and publish

```csharp
// DI registration
services.AddEventBus();
services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();

// Publish
var bus = serviceProvider.GetRequiredService<IEventBus>();
await bus.PublishAsync(new OrderPlaced(orderId, 99.99m));
```

### Pipeline behaviors

```csharp
public class LoggingBehavior : IEventPipelineBehavior
{
    public async Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken ct = default)
    {
        Console.WriteLine($"Handling {@event.GetType().Name}...");
        await next();
        Console.WriteLine($"Handled {@event.GetType().Name}");
    }
}

services.AddEventPipelineBehavior<LoggingBehavior>();
```

### Deduplication

```csharp
services.AddEventDeduplication(ttl: TimeSpan.FromMinutes(30));
```

### Manual subscriptions

```csharp
var bus = serviceProvider.GetRequiredService<IEventBus>();
var subscription = bus.Subscribe(new OrderPlacedHandler());
// Later: subscription.Dispose() to unsubscribe
```

### Topic conventions

```csharp
// Default: "events.order-placed" or "orders.order-placed" (with Source)
var convention = new DefaultTopicConvention();
var topic = convention.GetTopic(typeof(OrderPlaced));

// Attribute-based:
[Topic("custom.orders.placed")]
public sealed record OrderPlaced(...) : EventBase { ... }
```

## Dependencies

- **Microsoft.Extensions.DependencyInjection.Abstractions** — DI registration (provided by consuming project)

## License

[MIT](License.md)

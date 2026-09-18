# Birko.EventBus.MessageQueue

Distributed event bus backed by Birko.MessageQueue providers. Publishes strongly-typed events over any transport (InMemory, MQTT, RabbitMQ, Kafka) and dispatches received events to registered handlers.

## Features

- **Transport-agnostic** — Works with any Birko.MessageQueue provider
- **Event envelope** — Wraps events with metadata (type, correlation, tenant) for transport
- **Auto-subscription** — Scans DI for `IEventHandler<T>` and creates transport subscriptions on startup
- **Topic conventions** — Configurable event type → topic mapping via `ITopicConvention`
- **Pipeline integration** — Uses Birko.EventBus pipeline behaviors and enrichers
- **DI integration** — `AddDistributedEventBus()` with hosted service for auto-subscribe

## Usage

### Register distributed event bus

```csharp
// Register message queue first
services.AddSingleton<IMessageQueue>(new InMemoryMessageQueue());

// Register distributed event bus
services.AddDistributedEventBus(opts =>
{
    opts.AutoSubscribe = true;  // default
});

// Register handlers
services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();
```

### Publish events

```csharp
var bus = serviceProvider.GetRequiredService<IEventBus>();
await bus.PublishAsync(new OrderPlaced(orderId, 99.99m));
// Event is serialized as EventEnvelope and sent to topic "events.order-placed"
```

### Manual transport subscription

```csharp
var bus = (DistributedEventBus)serviceProvider.GetRequiredService<IEventBus>();
await bus.SubscribeToTransportAsync<OrderPlaced>();
```

### Custom topic convention

```csharp
services.AddDistributedEventBus(opts =>
{
    opts.TopicConvention = new AttributeTopicConvention();
});

[Topic("custom.orders.placed")]
public sealed record OrderPlaced(...) : EventBase { ... }
```

## Transport Matrix

| Transport | Use Case | Persistence | Ordering |
|-----------|----------|-------------|----------|
| InMemory | Tests, dev | No | Per-destination |
| MQTT | IoT events | Broker-dependent | Per-topic |
| RabbitMQ | General events | Yes | Per-queue |
| Kafka | High-throughput | Yes | Per-partition |

## Dependencies

- **Birko.EventBus** — Core interfaces
- **Birko.MessageQueue** — Transport abstraction
- **Microsoft.Extensions.DependencyInjection.Abstractions** — DI
- **Microsoft.Extensions.Hosting.Abstractions** — IHostedService

## License

[MIT](License.md)

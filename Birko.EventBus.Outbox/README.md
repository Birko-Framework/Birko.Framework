# Birko.EventBus.Outbox

Transactional outbox pattern for Birko.EventBus. Events are written to a persistent store in the same transaction as business data, then published asynchronously by a background processor. Guarantees at-least-once delivery.

## Features

- **Transactional consistency** — Event persisted in same DB transaction as business data
- **At-least-once delivery** — Events survive process crashes (persisted in store)
- **Retry with failure tracking** — Failed publishes retried up to MaxAttempts
- **Automatic cleanup** — Old published/failed entries purged after RetentionPeriod
- **Decorator pattern** — OutboxEventBus wraps any IEventBus transparently
- **Pluggable store** — Implement IOutboxStore for your database (InMemory included for testing)

## Usage

### Register outbox

```csharp
// Register inner event bus first
services.AddEventBus();

// Add outbox with in-memory store (for testing)
services.AddInMemoryOutbox(opts =>
{
    opts.BatchSize = 50;
    opts.PollingInterval = TimeSpan.FromSeconds(2);
    opts.MaxAttempts = 5;
    opts.RetentionPeriod = TimeSpan.FromDays(7);
});

// Wrap IEventBus with outbox decorator
services.AddOutboxEventBus();
```

### Publish (transparent to caller)

```csharp
var bus = serviceProvider.GetRequiredService<IEventBus>();
// This writes to outbox store, NOT directly to the bus
await bus.PublishAsync(new OrderPlaced(orderId, total));
```

### Custom outbox store

```csharp
public class SqlOutboxStore : IOutboxStore
{
    // Implement SaveAsync, GetPendingAsync, MarkPublishedAsync, MarkFailedAsync, CleanupAsync
}

services.AddOutbox<SqlOutboxStore>();
services.AddOutboxEventBus();
```

## Flow

```
Module Code:
  await repository.CreateAsync(order);          // 1. Save business entity
  await eventBus.PublishAsync(new OrderPlaced()); // 2. OutboxEventBus writes to outbox store
  await unitOfWork.CommitAsync();               // 3. Both saved in same transaction

OutboxProcessor (background):
  Poll outbox WHERE Status = Pending            // 4. Find unsent events
  Publish via inner IEventBus                   // 5. Send to MessageQueue / in-process
  Update Status = Published                     // 6. Mark as done
```

## Dependencies

- **Birko.EventBus** — Core interfaces
- **Birko.MessageQueue** — IMessageSerializer for payload serialization
- **Microsoft.Extensions.DependencyInjection.Abstractions** — DI
- **Microsoft.Extensions.Hosting.Abstractions** — BackgroundService

## License

[MIT](License.md)

# Birko.EventBus.Outbox

## Overview
Transactional outbox pattern for Birko.EventBus. Persists events in a store, publishes asynchronously via background processor.

## Project Location
- **Directory:** `C:\Source\Birko.EventBus.Outbox\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.EventBus.Outbox`

## Components

| File | Description |
|------|-------------|
| Core/IOutboxStore.cs | Persistence interface: Save, GetPending, MarkPublished, MarkFailed, Cleanup |
| Core/OutboxEntry.cs | Persisted event: EventId, EventType, Payload, Source, CorrelationId, TenantGuid, Status, Attempts |
| Core/OutboxStatus.cs | Enum: Pending, Publishing, Published, Failed |
| Core/OutboxOptions.cs | BatchSize (100), PollingInterval (5s), MaxAttempts (5), RetentionPeriod (7d), CleanupInterval (1h) |
| Stores/InMemoryOutboxStore.cs | ConcurrentDictionary-based store for testing |
| Publishing/OutboxEventBus.cs | IEventBus decorator — writes to outbox instead of publishing directly |
| Publishing/OutboxProcessor.cs | Polls pending entries, deserializes, publishes via inner bus, marks published/failed |
| Hosting/OutboxProcessorHostedService.cs | BackgroundService that runs OutboxProcessor in a loop |
| Extensions/OutboxServiceCollectionExtensions.cs | AddOutbox&lt;TStore&gt;(), AddInMemoryOutbox(), AddOutboxEventBus() |

## Architecture

```
OutboxEventBus (decorator)
  PublishAsync → serialize event → IOutboxStore.SaveAsync (in same DB transaction)

OutboxProcessor (background)
  IOutboxStore.GetPendingAsync → deserialize → inner IEventBus.PublishAsync → MarkPublished/MarkFailed
```

## Dependencies
- Birko.EventBus — Core interfaces (IEventBus, IEvent, IEventHandler)
- Birko.MessageQueue — IMessageSerializer
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Hosting.Abstractions (BackgroundService)

## Important Notes
- OutboxEventBus wraps an inner IEventBus — Subscribe delegates to inner, PublishAsync writes to store
- OutboxProcessor uses reflection to call PublishAsync&lt;TEvent&gt; with the correct generic type
- **Cleanup cadence (CR-L259):** the background loop calls `ProcessBatchAsync` every `PollingInterval`, but the retention prune (`CleanupIfDueAsync`) is throttled to `CleanupInterval` (default 1h) — a full retention scan/delete does not run every poll. `CleanupAsync` still runs unconditionally if called directly. `OutboxProcessor` takes an optional `IDateTimeProvider` for testable cadence
- **Registration order-independent (CR-L260):** `AddOutbox` and `AddOutboxEventBus` may be called in either order — the `OutboxProcessor` factory resolves `IEventBus` lazily and unwraps the `OutboxEventBus` decorator to its inner bus, so the processor always publishes through the real transport, never back into the outbox. `AddOutbox` registers the processor/store/hosted service; `AddOutboxEventBus` is the one that decorates `IEventBus`
- MarkFailed honors the configured `OutboxOptions.MaxAttempts` cap (passed into `IOutboxStore.MarkFailedAsync`), not a hardcoded value (CR-H115)
- SQL store not included — implement IOutboxStore for your database

## Maintenance
- When adding new files, update the .projitems file
- If adding SQL/MongoDB outbox stores, consider separate projects to avoid heavy dependencies

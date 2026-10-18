# Birko.MessageQueue.InMemory

## Overview
In-memory message queue implementation using `System.Threading.Channels`. For testing and development — messages are not persisted.

## Project Location
- **Directory:** `Birko.MessageQueue.InMemory/`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.MessageQueue.InMemory`

## Components

| File | Description |
|------|-------------|
| InMemoryMessageQueue.cs | IMessageQueue implementation — creates producer, consumer, manages connection state |
| InMemoryMessageQueueOptions.cs | Configuration (channel capacity); consumed via the `InMemoryMessageQueue(InMemoryMessageQueueOptions, …)` ctor (CR-L283) |
| InMemoryChannel.cs | Internal channel manager — bounded channels per destination, subscriber dispatch loop |
| InMemoryProducer.cs | IMessageProducer — writes to channels, supports delayed delivery via Task.Delay |
| InMemoryConsumer.cs | IMessageConsumer — subscribes to channels, typed deserialization, ack/reject tracking |
| InMemorySubscription.cs | ISubscription — unsubscribe removes handler from channel |

## Architecture

```
InMemoryMessageQueue
├── InMemoryProducer  -> writes to InMemoryChannel
├── InMemoryConsumer  -> subscribes to InMemoryChannel
└── InMemoryChannel (internal)
    └── DestinationState per destination
        ├── Channel<QueueMessage> (bounded)
        └── ConcurrentDictionary<Guid, handler> (subscribers)
```

- Each destination gets a `BoundedChannel<QueueMessage>` with configurable capacity
- When subscribers exist, a dispatch loop reads from the channel and invokes all handlers
- When no subscribers, messages buffer in the channel for later consumption
- `InMemoryProducer` handles delayed messages via `Task.Delay` before writing
- `TimeToLive` (TASK-668): an expired message (from `CreatedAt`, on `InMemoryMessageQueueOptions.Clock`) is dropped at dispatch and by `ReadAsync`
- `PrefetchCount` (TASK-668): above 1, up to that many of a subscription's handlers run at once (a `SemaphoreSlim` per subscriber; the loop waits for a slot); 1 (default) is awaited in the loop, one at a time
- `IRedeliveryCapability` = `Resend`: no redelivery of a failed delivery, so the distributed bus retries by a delayed re-send

## Dependencies
- **Birko.MessageQueue** — Core interfaces
- **System.Threading.Channels** — Built-in

## Maintenance
- When adding new files, update the .projitems file
- This is a testing/dev implementation — keep it simple, no persistence

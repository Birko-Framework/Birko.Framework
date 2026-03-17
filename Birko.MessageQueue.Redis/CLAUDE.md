# Birko.MessageQueue.Redis

## Overview
Redis Streams implementation of Birko.MessageQueue interfaces. Provides persistent, ordered messaging using Redis Streams with consumer group support.

## Project Location
`C:\Source\Birko.MessageQueue.Redis\` (Shared project via .projitems)

## Components

### RedisStreamQueue.cs
- Main entry point implementing `IMessageQueue`
- Dual constructors: `RedisStreamSettings` or `RedisConnectionManager` + settings
- Owns or borrows `RedisConnectionManager` (tracks via `_ownsConnection`)
- Creates `RedisProducer` and `RedisConsumer` internally

### RedisProducer.cs
- Implements `IMessageProducer`
- Uses `XADD` to append messages to streams
- Stores message fields: id, body, payload_type, headers, created_at, priority, message (full serialized)
- Supports `MAXLEN ~` approximate trimming
- TTL stored as field for consumer-side expiry check

### RedisConsumer.cs
- Implements `IMessageConsumer`
- `XREADGROUP` for consumer group subscriptions, `XREAD` for simple subscriptions
- Background polling loop per subscription
- Auto-creates consumer groups via `XGROUP CREATE ... MKSTREAM`
- Handles `BUSYGROUP` error (group already exists)
- Manual ack via `XACK`, auto-ack on handler success
- Pending entry recovery when `FromBeginning = true`
- TTL enforcement (expired messages auto-acked and skipped)

### RedisSubscription.cs
- Implements `ISubscription`
- Cancels polling loop on Unsubscribe/Dispose

### RedisStreamSettings.cs
- Extends `RedisSettings` with stream-specific options
- `ConsumerGroup`, `ConsumerName` — consumer group configuration
- `ReadCount` — messages per XREAD call (default 10)
- `BlockMilliseconds` — poll interval (default 5000ms)
- `MaxStreamLength` — MAXLEN trimming threshold
- `AutoCreateConsumerGroup` — auto XGROUP CREATE (default true)
- `StreamPrefix` — key prefix for stream keys (default "birko:mq:stream")

## Dependencies
- **Birko.MessageQueue** — IMessageQueue, IMessageProducer, IMessageConsumer, QueueMessage, etc.
- **Birko.Redis** — RedisSettings, RedisConnectionManager
- **StackExchange.Redis** — Redis client (IDatabase, StreamEntry, etc.)

## Key Patterns
- Connection ownership tracking (`_ownsConnection`)
- Lazy connection via `RedisConnectionManager`
- Polling loop with cancellation token per subscription
- Lua-free: uses native Redis Streams commands (no custom Lua scripts needed)
- Consumer group auto-creation with BUSYGROUP error handling

## Maintenance
When modifying this project, update:
- This CLAUDE.md if components or patterns change
- README.md if public API or usage changes
- docs/message-queue.md for framework-wide documentation
- TODO.md status (change from Planned to Done)

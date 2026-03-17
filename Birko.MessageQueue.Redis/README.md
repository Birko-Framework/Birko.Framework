# Birko.MessageQueue.Redis

Redis Streams implementation of `Birko.MessageQueue` interfaces using StackExchange.Redis.

## Overview

Provides a persistent, ordered message queue using Redis Streams (`XADD`, `XREAD`, `XREADGROUP`, `XACK`). Supports consumer groups for load-balanced message processing, automatic acknowledgment, message expiry via TTL, and stream trimming.

## Features

- **Redis Streams** — Append-only log with persistent message storage
- **Consumer Groups** — Load-balanced message distribution across consumers via `XREADGROUP`
- **Acknowledgment** — Manual (`XACK`) and automatic acknowledgment modes
- **Message Trimming** — Approximate `MAXLEN` trimming to cap stream size
- **TTL Expiry** — Per-message time-to-live, expired messages auto-skipped
- **Pending Entry Recovery** — Automatic processing of pending entries on startup with `FromBeginning`
- **Pluggable Serialization** — Default JSON, or bring your own `IMessageSerializer`
- **Connection Sharing** — Accept external `RedisConnectionManager` or create internally

## Usage

```csharp
using Birko.MessageQueue;
using Birko.MessageQueue.Redis;

// Configure settings
var settings = new RedisStreamSettings("localhost", 6379)
{
    ConsumerGroup = "my-service",
    ConsumerName = "worker-1",
    MaxStreamLength = 10000,
    StreamPrefix = "myapp:mq:stream"
};

// Create queue
await using var queue = new RedisStreamQueue(settings);
await queue.ConnectAsync();

// Produce messages
await queue.Producer.SendAsync("orders", new QueueMessage
{
    Body = "{\"orderId\": 123}",
    Priority = 1
});

// Or send typed payloads
await queue.Producer.SendAsync("orders", new OrderPlaced { OrderId = 123 });

// Subscribe to messages
var subscription = await queue.Consumer.SubscribeAsync<OrderPlaced>(
    "orders",
    new OrderHandler(),
    new ConsumerOptions
    {
        AckMode = MessageAckMode.AutoAck,
        GroupId = "order-processors"
    });

// Unsubscribe when done
await subscription.UnsubscribeAsync();
```

### Shared Connection

```csharp
using Birko.Redis;

var connectionManager = new RedisConnectionManager(settings);
var queue1 = new RedisStreamQueue(connectionManager, settings1);
var queue2 = new RedisStreamQueue(connectionManager, settings2);
```

## Dependencies

- **Birko.MessageQueue** — Core interfaces (`IMessageQueue`, `IMessageProducer`, `IMessageConsumer`)
- **Birko.Redis** — `RedisSettings`, `RedisConnectionManager`
- **StackExchange.Redis** — Redis client library

## License

MIT License - see [License.md](License.md)

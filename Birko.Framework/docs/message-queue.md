# Message Queue Guide

## Overview

Birko.MessageQueue provides core abstractions for asynchronous messaging between services. It supports pub/sub (one-to-many) and point-to-point (one-to-one) messaging patterns with pluggable transport backends.

## Architecture

```
IMessageQueue (combined interface)
├── IMessageProducer (send)
│   ├── IPublisher (pub/sub — one-to-many)
│   ├── ISender (point-to-point — one-to-one)
│   └── ITransactionalProducer (atomic batches)
└── IMessageConsumer (receive)
    ├── ISubscriber (pub/sub — typed lambda)
    └── IReceiver (point-to-point — pull-based)
```

## Core Interfaces

### IMessageQueue

Combined interface providing producer, consumer, and connection management:

```csharp
public interface IMessageQueue : IDisposable
{
    IMessageProducer Producer { get; }
    IMessageConsumer Consumer { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
```

### IMessageProducer

Sends messages to a queue or topic:

```csharp
public interface IMessageProducer : IDisposable
{
    Task SendAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default);
    Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class;
}
```

### IMessageConsumer

Subscribes to messages and manages acknowledgment:

```csharp
public interface IMessageConsumer : IDisposable
{
    Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default);
    Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class;
    Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default);
}
```

### IMessageHandler\<T\>

Typed message handler invoked for each received message:

```csharp
public interface IMessageHandler<T> where T : class
{
    Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken = default);
}
```

### ISubscription

Active subscription handle. Dispose to unsubscribe:

```csharp
public interface ISubscription : IDisposable
{
    string Destination { get; }
    bool IsActive { get; }
    Task UnsubscribeAsync(CancellationToken cancellationToken = default);
}
```

## Message Types

### QueueMessage

The message wrapper sent through the queue:

| Property | Type | Description |
|----------|------|-------------|
| Id | Guid | Unique message identifier |
| Body | string | Serialized payload |
| PayloadType | string? | .NET type name for deserialization |
| Headers | MessageHeaders | Metadata |
| CreatedAt | DateTimeOffset | Creation timestamp |
| Delay | TimeSpan? | Visibility delay |
| TimeToLive | TimeSpan? | Message expiration |
| Priority | int | Higher = more urgent (default 0) |

### MessageHeaders

Message metadata:

| Property | Type | Description |
|----------|------|-------------|
| CorrelationId | string? | Tracks related messages across services |
| ReplyTo | string? | Reply destination (request-reply pattern) |
| ContentType | string | Body format (default: "application/json") |
| GroupId | string? | Session/group ID for ordered delivery |
| Custom | Dictionary\<string, string\> | Arbitrary key-value pairs |

### MessageContext

Runtime context available to handlers:

| Property | Type | Description |
|----------|------|-------------|
| Message | QueueMessage | The original message |
| Destination | string | Source queue/topic |
| Consumer | IMessageConsumer | For manual ack/reject |
| DeliveryCount | int | Number of deliveries (1 = first) |
| ReceivedAt | DateTimeOffset | When received |

## Messaging Patterns

### Pub/Sub (One-to-Many)

All subscribers receive every message published to a topic.

```csharp
// Publisher
public interface IPublisher : IMessageProducer
{
    Task PublishAsync<T>(string topic, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class;
}

// Subscriber
public interface ISubscriber : IMessageConsumer
{
    Task<ISubscription> SubscribeAsync<T>(string topic, Func<T, MessageContext, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class;
}
```

### Point-to-Point (One-to-One)

Only one receiver processes each message.

```csharp
// Sender
public interface ISender : IMessageProducer
{
    Task SendToQueueAsync<T>(string queue, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class;
}

// Receiver (pull-based)
public interface IReceiver : IMessageConsumer
{
    Task<QueueMessage?> ReceiveAsync(string queue, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Task<T?> ReceiveAsync<T>(string queue, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where T : class;
}
```

## Consumer Options

```csharp
var options = new ConsumerOptions
{
    AckMode = MessageAckMode.ManualAck,  // AutoAck or ManualAck
    PrefetchCount = 10,                   // Unacked message limit (0 = unlimited)
    GroupId = "order-processors",          // Consumer group for load balancing
    FromBeginning = false                  // Start from latest (true = replay all)
};
```

### Acknowledgment Modes

| Mode | Behavior |
|------|----------|
| **AutoAck** | Message acknowledged when handler completes without exception |
| **ManualAck** | Must call `AcknowledgeAsync()` or `RejectAsync()` explicitly |

## Serialization

Pluggable message body serialization via `IMessageSerializer`:

```csharp
public interface IMessageSerializer
{
    string Serialize(object payload);
    object? Deserialize(string data, Type type);
    T? Deserialize<T>(string data) where T : class;
    string ContentType { get; }  // e.g., "application/json"
}
```

Default implementation: `JsonMessageSerializer` using System.Text.Json.

### Encrypted Serialization

`EncryptingMessageSerializer` is a decorator that encrypts/decrypts around any inner serializer. It takes `Func<string, string>` delegates to stay independent of any encryption library:

```csharp
// With Birko.Security AES-256-GCM
var aes = new AesEncryptionProvider();
var key = AesEncryptionProvider.GenerateKey();

var serializer = new EncryptingMessageSerializer(
    new JsonMessageSerializer(),
    plaintext => aes.EncryptString(plaintext, key),
    ciphertext => aes.DecryptString(ciphertext, key));

// All messages are now encrypted end-to-end
var queue = new MqttMessageQueue(options, serializer);
```

ContentType becomes `"application/json+encrypted"` so consumers know to decrypt.

## Message Deduplication

`MessageFingerprint` generates SHA256 content hashes for idempotency:

```csharp
// Fingerprint from body content
var fp = MessageFingerprint.Compute(message);
// "a1b2c3d4e5f6..."

// Destination-scoped fingerprint (same body, different destinations = different fingerprint)
var scoped = MessageFingerprint.Compute("orders.created", message.Body);
```

Use fingerprints to detect and skip duplicate messages in consumers.

## Retry & Dead Letter

### RetryPolicy

Configures exponential backoff for failed message deliveries:

```csharp
var policy = new RetryPolicy
{
    MaxRetries = 5,
    BaseDelay = TimeSpan.FromSeconds(2),
    MaxDelay = TimeSpan.FromMinutes(10),
    UseExponentialBackoff = true
};

// Delay calculation: BaseDelay * 2^(attempt-1), capped at MaxDelay
// Attempt 1: 2s, Attempt 2: 4s, Attempt 3: 8s, Attempt 4: 16s, Attempt 5: 32s
```

Built-in presets:
- `RetryPolicy.Default` — 3 retries, 5s base, 5min max, exponential
- `RetryPolicy.None` — No retries

### DeadLetterOptions

Routes messages that exceed retry limits to a dead letter queue:

```csharp
var dlq = new DeadLetterOptions
{
    Enabled = true,
    Suffix = ".dlq"           // orders.created -> orders.created.dlq
    // Or explicit destination:
    // Destination = "my-dead-letters"
};
```

## Transactions

`ITransactionalProducer` supports atomic message batches — messages are only visible after commit:

```csharp
public interface ITransactionalProducer : IMessageProducer
{
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
```

## Usage Examples

### Publishing and Subscribing

```csharp
// Connect
await queue.ConnectAsync();

// Publish a message
await queue.Producer.SendAsync("orders.created", new OrderCreated
{
    OrderId = Guid.NewGuid(),
    CustomerId = customerId,
    Total = 99.99m
});

// Subscribe with a handler class
var sub = await queue.Consumer.SubscribeAsync<OrderCreated>(
    "orders.created",
    new OrderCreatedHandler(),
    new ConsumerOptions { AckMode = MessageAckMode.AutoAck });

// Subscribe with a lambda
var sub2 = await queue.Consumer.SubscribeAsync(
    "orders.created",
    async (message, ct) =>
    {
        Console.WriteLine($"Received: {message.Body}");
        await queue.Consumer.AcknowledgeAsync(message.Id, ct);
    },
    new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

// Unsubscribe
await sub.UnsubscribeAsync();
// Or: sub.Dispose();
```

### Implementing a Message Handler

```csharp
public class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, MessageContext context, CancellationToken cancellationToken = default)
    {
        // Process the order
        Console.WriteLine($"Order {message.OrderId} from customer {message.CustomerId}");

        // Manual ack if needed
        if (context.Message.Headers.Custom.ContainsKey("require-ack"))
        {
            await context.Consumer.AcknowledgeAsync(context.Message.Id, cancellationToken);
        }
    }
}
```

### Message with Headers

```csharp
var headers = new MessageHeaders
{
    CorrelationId = Guid.NewGuid().ToString(),
    ReplyTo = "orders.replies",
    GroupId = "customer-123"
};
headers.Custom["source"] = "web-api";
headers.Custom["version"] = "2";

await queue.Producer.SendAsync("orders.created", order, headers);
```

### Priority and Delayed Messages

```csharp
var message = new QueueMessage
{
    Body = serializer.Serialize(payload),
    Priority = 10,                              // High priority
    Delay = TimeSpan.FromMinutes(5),            // Visible after 5 minutes
    TimeToLive = TimeSpan.FromHours(1)          // Expires after 1 hour
};

await queue.Producer.SendAsync("notifications", message);
```

## Available Implementations

| Package | Transport | Status |
|---------|-----------|--------|
| Birko.MessageQueue.InMemory | In-process channels | Available |
| Birko.MessageQueue.MQTT | MQTTnet | Available |
| Birko.MessageQueue.MQTT | MQTTnet | High (Phase 5) |
| Birko.MessageQueue.RabbitMQ | AMQP 0-9-1 | Medium (Phase 8) |
| Birko.MessageQueue.Kafka | Confluent.Kafka | Medium (Phase 8) |
| Birko.MessageQueue.Azure | Azure Service Bus | Low (Phase 8) |
| Birko.MessageQueue.Aws | AWS SQS | Low (Phase 8) |
| Birko.MessageQueue.Redis | Redis Streams | Low (Phase 8) |
| Birko.MessageQueue.MassTransit | MassTransit wrapper | Low (Phase 8) |

## Implementing a Custom Backend

To add a new transport backend:

1. Create a new shared project (e.g., `Birko.MessageQueue.MyTransport`)
2. Implement `IMessageQueue`, `IMessageProducer`, `IMessageConsumer`, `ISubscription`
3. Optionally implement pattern interfaces (`IPublisher`, `ISubscriber`, `ISender`, `IReceiver`)
4. Optionally implement `ITransactionalProducer` if the transport supports transactions
5. Use `IMessageSerializer` for payload serialization (accept via constructor)
6. Apply `RetryPolicy` and `DeadLetterOptions` as appropriate for the transport

## Dependencies

- **Birko.MessageQueue** — None (System.Text.Json built-in only)
- Transport backends depend on their respective client libraries

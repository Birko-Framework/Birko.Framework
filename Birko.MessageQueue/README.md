# Birko.MessageQueue

Core message queue abstractions for the Birko Framework. Provides interfaces for asynchronous messaging between services using pub/sub and point-to-point patterns.

## Features

- **IMessageQueue** — Combined producer/consumer with connection management
- **IMessageProducer** — Send messages to queues or topics
- **IMessageConsumer** — Subscribe to destinations with typed handlers
- **Pub/Sub pattern** — IPublisher/ISubscriber for one-to-many messaging
- **Point-to-point pattern** — ISender/IReceiver for one-to-one messaging
- **Pluggable serialization** — IMessageSerializer with JSON default
- **Encrypted serialization** — EncryptingMessageSerializer decorator (works with Birko.Security AES)
- **Message fingerprinting** — SHA256 content hashing for deduplication
- **Retry policies** — Exponential backoff with configurable limits (RetryPolicy from Birko.Contracts, shared across framework)
- **Dead letter queues** — Configurable DLQ routing for failed messages
- **Transactional sends** — ITransactionalProducer for atomic message batches
- **Manual/auto acknowledgment** — Control when messages are considered processed

## Architecture

```
IMessageQueue (combined interface)
├── IMessageProducer (send)
│   ├── IPublisher (pub/sub)
│   ├── ISender (point-to-point)
│   └── ITransactionalProducer (transactional)
└── IMessageConsumer (receive)
    ├── ISubscriber (pub/sub)
    └── IReceiver (point-to-point)
```

## Available Implementations

| Package | Transport | Status |
|---------|-----------|--------|
| Birko.MessageQueue.InMemory | In-process channels | Planned |
| Birko.MessageQueue.MQTT | MQTTnet | Planned |
| Birko.MessageQueue.RabbitMQ | AMQP 0-9-1 | Planned |
| Birko.MessageQueue.Kafka | Confluent.Kafka | Planned |
| Birko.MessageQueue.Azure | Azure Service Bus | Planned |
| Birko.MessageQueue.Aws | AWS SQS | Planned |
| Birko.MessageQueue.Redis | Redis Streams | Planned |
| Birko.MessageQueue.MassTransit | MassTransit wrapper | Planned |

## Usage

### Publishing messages (pub/sub)

```csharp
// Connect to broker
await queue.ConnectAsync();

// Send a typed message
await queue.Producer.SendAsync("orders.created", new OrderCreated
{
    OrderId = orderId,
    CustomerId = customerId
});
```

### Subscribing to messages

```csharp
// Subscribe with a typed handler
var subscription = await queue.Consumer.SubscribeAsync<OrderCreated>(
    "orders.created",
    new OrderCreatedHandler(),
    new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

// Or with a lambda
var subscription = await queue.Consumer.SubscribeAsync(
    "orders.created",
    async (message, ct) =>
    {
        // Process message
        await queue.Consumer.AcknowledgeAsync(message.Id, ct);
    });
```

### Custom serialization

```csharp
var serializer = new JsonMessageSerializer(new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
```

### Encrypted messages (with Birko.Security)

```csharp
var aes = new AesEncryptionProvider();
var key = AesEncryptionProvider.GenerateKey();

var serializer = new EncryptingMessageSerializer(
    new JsonMessageSerializer(),
    plaintext => aes.EncryptString(plaintext, key),
    ciphertext => aes.DecryptString(ciphertext, key));

// Messages are now encrypted in transit
var queue = new InMemoryMessageQueue(serializer: serializer);
```

### Message deduplication

```csharp
// Content-based fingerprint
var fingerprint = MessageFingerprint.Compute(message);

// Destination-scoped fingerprint
var scoped = MessageFingerprint.Compute("orders.created", message.Body);
```

### Retry policy

```csharp
var policy = new RetryPolicy
{
    MaxRetries = 5,
    BaseDelay = TimeSpan.FromSeconds(2),
    MaxDelay = TimeSpan.FromMinutes(10),
    UseExponentialBackoff = true
};
```

### Dead letter queue

```csharp
var dlqOptions = new DeadLetterOptions
{
    Enabled = true,
    Suffix = ".dlq" // orders.created -> orders.created.dlq
};
```

## Dependencies

- Birko.Contracts (provides RetryPolicy, shared across framework)
- System.Text.Json (built-in) for the default serializer

## License

[MIT](License.md)

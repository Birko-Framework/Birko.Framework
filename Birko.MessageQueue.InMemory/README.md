# Birko.MessageQueue.InMemory

In-memory message queue implementation for the Birko Framework. Uses `System.Threading.Channels` for fast, in-process message delivery. Designed for testing and development — messages are lost on process restart.

## Features

- Channel-based async message delivery
- Pub/Sub support (multiple subscribers per destination)
- Auto and manual acknowledgment modes
- Configurable channel capacity with backpressure
- Delayed message delivery
- No external dependencies

## Usage

### Basic pub/sub

```csharp
var queue = new InMemoryMessageQueue();
await queue.ConnectAsync();

// Subscribe
var subscription = await queue.Consumer.SubscribeAsync<OrderCreated>(
    "orders.created",
    new OrderCreatedHandler());

// Publish
await queue.Producer.SendAsync("orders.created", new OrderCreated
{
    OrderId = Guid.NewGuid(),
    Total = 49.99m
});

// Cleanup
await subscription.UnsubscribeAsync();
queue.Dispose();
```

### Manual acknowledgment

```csharp
var subscription = await queue.Consumer.SubscribeAsync(
    "orders",
    async (message, ct) =>
    {
        // Process message...
        await queue.Consumer.AcknowledgeAsync(message.Id, ct);
    },
    new ConsumerOptions { AckMode = MessageAckMode.ManualAck });
```

### Custom capacity

```csharp
// Buffer up to 5000 messages per destination before backpressure
var queue = new InMemoryMessageQueue(channelCapacity: 5000);
```

### Custom serializer

```csharp
var serializer = new JsonMessageSerializer(new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
var queue = new InMemoryMessageQueue(serializer: serializer);
```

## Dependencies

- **Birko.MessageQueue** — Core interfaces
- **System.Threading.Channels** — Built-in .NET

## License

[MIT](License.md)

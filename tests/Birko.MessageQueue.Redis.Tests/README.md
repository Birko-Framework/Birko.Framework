# Birko.MessageQueue.Redis.Tests

Unit tests for the Birko.MessageQueue.Redis project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **.NET 10.0**

## Test Coverage

- **RedisStreamSettingsTests** — Settings defaults, constructor parameters, stream key generation, connection string building
- **RedisStreamQueueTests** — Constructor validation, producer/consumer creation, dispose behavior, connect/disconnect lifecycle
- **RedisProducerTests** — Destination validation, dispose behavior
- **RedisConsumerTests** — Destination validation, dispose behavior, ack/reject with unknown message IDs
- **RedisSubscriptionTests** — Subscribe/unsubscribe lifecycle, IsActive tracking, dispose behavior

## Running Tests

```bash
dotnet test Birko.MessageQueue.Redis.Tests/
```

## License

MIT License - see [License.md](License.md)

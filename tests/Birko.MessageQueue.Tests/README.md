# Birko.MessageQueue.Tests

Unit tests for the Birko.MessageQueue ecosystem — core interfaces, InMemory backend, MQTT topic utilities, and serialization.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **.NET 10.0**

## Test Categories

### Core Tests
- `QueueMessageTests` — Default values, unique IDs, property setting
- `MessageHeadersTests` — Default values, custom header entries
- `MessageContextTests` — Property assignment, null handling
- `MessageFingerprintTests` — SHA256 hashing, determinism, scoping
- `ConsumerOptionsTests` — Default option values

### Serialization Tests
- `JsonMessageSerializerTests` — Content type, serialize/deserialize, round-trip
- `EncryptingMessageSerializerTests` — Decorator pattern, encryption/decryption, null guards

### InMemory Tests
- `InMemoryMessageQueueTests` — Connect/disconnect, pub/sub, typed messages, multiple subscribers, unsubscribe, manual ack
- `MqttTopicTests` — Publish topic validation, subscribe filter validation, wildcard matching

## Running Tests

```bash
dotnet test
```

## License

MIT License - see [License.md](License.md)

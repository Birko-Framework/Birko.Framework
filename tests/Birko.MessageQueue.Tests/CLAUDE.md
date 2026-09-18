# Birko.MessageQueue.Tests

## Overview
Unit tests for the Birko.MessageQueue ecosystem covering core interfaces, InMemory backend, MQTT topic utilities, and serialization.

## Project Location
`C:\Source\Birko.MessageQueue.Tests\`

## Components

### Core Tests (`Core/`)
- **QueueMessageTests.cs** — QueueMessage defaults, unique IDs, property setting
- **MessageHeadersTests.cs** — MessageHeaders defaults, custom entries
- **MessageContextTests.cs** — MessageContext property assignment
- **MessageFingerprintTests.cs** — SHA256 fingerprinting determinism and scoping
- **ConsumerOptionsTests.cs** — ConsumerOptions default values

### Serialization Tests (`Serialization/`)
- **JsonMessageSerializerTests.cs** — JSON serialization round-trip, content type
- **EncryptingMessageSerializerTests.cs** — Decorator encryption/decryption, null guards

### InMemory Tests (`InMemory/`)
- **InMemoryMessageQueueTests.cs** — Full pub/sub lifecycle, typed messages, ack/reject
- **MqttTopicTests.cs** — MQTT topic validation and wildcard matching (static utility, no broker)

## Dependencies
- **Birko.MessageQueue** (projitems) — Core interfaces and types
- **Birko.MessageQueue.InMemory** (projitems) — InMemory backend
- **Birko.MessageQueue.MQTT** (projitems) — MQTT topic utilities
- **MQTTnet** 4.3.7.1207 — Required by MQTT projitems
- **xUnit** 2.9.3, **FluentAssertions** 7.0.0

## Notes
- Tests use `CancellationToken.None` explicitly when calling `SendAsync` with `QueueMessage` to disambiguate from the generic `SendAsync<T>` overload
- MqttTopicTests test only the static `MqttTopic` utility class (no MQTT broker needed)
- Per MQTT spec 4.7.1.2, `sensors/#` matches `sensors` (multi-level wildcard matches parent)

## Maintenance
When adding new MessageQueue features or backends, add corresponding tests here.

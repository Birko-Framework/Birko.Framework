# Birko.MessageQueue

## Overview
Core message queue abstractions for the Birko Framework. Provides interfaces for asynchronous messaging (pub/sub, point-to-point) that are implemented by transport-specific projects (MQTT, RabbitMQ, Kafka, etc.).

## Project Location
- **Directory:** `C:\Source\Birko.MessageQueue\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.MessageQueue`

## Components

### Core/ — Foundation types
| File | Description |
|------|-------------|
| IMessageQueue.cs | Combined interface (Producer + Consumer + connection management) |
| IMessageProducer.cs | Send messages to destinations |
| IMessageConsumer.cs | Subscribe to destinations, ack/reject messages |
| IMessageHandler.cs | Typed message handler interface |
| ISubscription.cs | Active subscription handle (dispose to unsubscribe) |
| QueueMessage.cs | Message wrapper (Id, Body, PayloadType, Headers, Priority, TTL, Delay) |
| MessageHeaders.cs | Metadata (CorrelationId, ReplyTo, ContentType, GroupId, Custom dictionary) |
| MessageContext.cs | Runtime context for handlers (Message, Destination, Consumer, DeliveryCount) |
| ConsumerOptions.cs | Subscription config (AckMode, PrefetchCount, GroupId, FromBeginning) |
| MessageAckMode.cs | Enum: AutoAck, ManualAck |
| MessageFingerprint.cs | SHA256 content fingerprinting for deduplication |

### Patterns/ — Messaging patterns
| File | Description |
|------|-------------|
| IPublisher.cs | Pub/Sub: one-to-many (extends IMessageProducer) |
| ISubscriber.cs | Pub/Sub: subscribe with typed lambda (extends IMessageConsumer) |
| ISender.cs | Point-to-point: one-to-one send (extends IMessageProducer) |
| IReceiver.cs | Point-to-point: pull-based receive (extends IMessageConsumer) |

### Serialization/ — Message body serialization
| File | Description |
|------|-------------|
| IMessageSerializer.cs | Serialize/deserialize + ContentType property |
| JsonMessageSerializer.cs | System.Text.Json default implementation |
| EncryptingMessageSerializer.cs | Decorator: encrypts/decrypts around inner serializer via Func delegates |

### Retry/ — Failure handling
| File | Description |
|------|-------------|
| DeadLetterOptions.cs | DLQ routing (suffix-based or explicit destination) |

### Transactions/ — Transactional messaging
| File | Description |
|------|-------------|
| ITransactionalProducer.cs | Begin/Commit/Rollback for atomic message batches |

## Dependencies
- Birko.Contracts — imported via projitems, provides RetryPolicy (namespace `Birko`)
- Birko.Serialization — JsonMessageSerializer delegates to ISerializer internally, accepts ISerializer in constructor

## Design Decisions
- **IMessageQueue combines Producer + Consumer** — Most brokers share a single connection for both. Implementations can expose only Producer or Consumer if needed.
- **ISubscription pattern** — Push-based subscription returns a disposable handle, matching how most brokers work (callbacks, not polling).
- **IReceiver adds pull-based receive** — Point-to-point pattern adds explicit ReceiveAsync for request-reply and batch processing scenarios.
- **Serialization is pluggable** — Each implementation can use its own serializer. ContentType header enables mixed formats.
- **RetryPolicy comes from Birko.Contracts** — Shared across framework (BackgroundJobs, MessageQueue, etc.). Retry is separate from ConsumerOptions because it's implementation-specific (some brokers handle it natively).
- **EncryptingMessageSerializer uses delegates** — Takes `Func<string,string>` encrypt/decrypt instead of depending on Birko.Security directly. Wires easily with `AesEncryptionProvider`.
- **MessageFingerprint uses System.Security.Cryptography** — SHA256, no external deps. Useful for idempotency keys and deduplication.

## Maintenance
- When adding new core interfaces, update the .projitems file
- Keep interfaces minimal — transport-specific features go in implementation projects
- All new code must compile without nullable reference type warnings

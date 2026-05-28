---
id: EPIC-006
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.MessageQueue.RabbitMQ, Birko.MessageQueue.Kafka, Birko.MessageQueue.Azure, Birko.MessageQueue.Aws, Birko.MessageQueue.MassTransit]
---

# Birko.MessageQueue — Provider expansion

## Area of concern

Add the major broker integrations beyond the existing MQTT / InMemory / Redis Streams providers — RabbitMQ (AMQP), Kafka, Azure Service Bus, AWS SQS, and a MassTransit adapter.

## Success criteria

- Five sibling shared projects exist and are registered
- Each implements `IMessageQueue` (publish, subscribe, ack/nack, dead-letter where applicable)
- Provider-specific features exposed via options (exchanges, partitions, sessions, FIFO)
- xUnit tests with Testcontainers or SDK mocks

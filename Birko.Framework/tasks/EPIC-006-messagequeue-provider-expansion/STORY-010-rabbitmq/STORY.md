---
id: STORY-010
parent: EPIC-006
status: planned
created: 2026-05-28
---

# RabbitMQ (AMQP)

## User story

As a developer, I want RabbitMQ as a Birko.MessageQueue provider for traditional AMQP pub/sub deployments.

## Behaviour

- Implements `IMessageQueue` over `RabbitMQ.Client`
- Exchanges, queues, publisher confirms, consumer ack
- Connection recovery on transient failures
- Dead-letter exchange support

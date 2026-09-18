---
id: STORY-017
parent: EPIC-008
status: planned
created: 2026-05-28
---

# Message queue health checks

## User story

As an operator, I want health checks for RabbitMQ and Kafka brokers so I know when they're unreachable before they break processing.

## Behaviour

- RabbitMQ check uses the management HTTP API (auth, vhost reachable, queue lengths optional)
- Kafka check uses a metadata request (cluster reachable, broker count, partition health optional)
- Each registers via Birko.Health DI extensions

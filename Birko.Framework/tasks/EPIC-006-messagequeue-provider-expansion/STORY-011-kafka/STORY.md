---
id: STORY-011
parent: EPIC-006
status: planned
created: 2026-05-28
---

# Kafka

## User story

As a developer building high-throughput streaming pipelines, I want Apache Kafka as a Birko.MessageQueue provider.

## Behaviour

- Implements `IMessageQueue` over `Confluent.Kafka`
- Topics, partitions, consumer groups, offset management
- Schema registry integration (optional)
- Idempotent producer support

---
id: EPIC-008
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Health.Data, Birko.Health.Azure, Birko.Health.Aws]
---

# Birko.Health — Queue + cloud health checks

## Area of concern

Health checks for the message queue providers added in EPIC-006, plus cloud-broker checks that fit naturally in `Birko.Health.Azure` and a new `Birko.Health.Aws`.

## Success criteria

- `RabbitMqHealthCheck` and `KafkaHealthCheck` in `Birko.Health.Data`
- `AzureServiceBusHealthCheck` in `Birko.Health.Azure`
- `AwsSqsHealthCheck` in `Birko.Health.Aws` (new project, scaffolded as part of this epic)
- Each follows the standard probe pattern (TCP / management API / SDK call) and registers via DI extensions

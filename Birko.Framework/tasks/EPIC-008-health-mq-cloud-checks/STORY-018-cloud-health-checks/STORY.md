---
id: STORY-018
parent: EPIC-008
status: planned
created: 2026-05-28
---

# Cloud queue health checks

## User story

As an operator, I want health checks for Azure Service Bus and AWS SQS that respect cloud-provider auth and throttling.

## Behaviour

- Azure Service Bus uses the management REST API
- AWS SQS uses `GetQueueAttributes` (cheapest valid call)
- Each registers via Birko.Health DI extensions
- A new `Birko.Health.Aws` sibling project gets scaffolded for the SQS check

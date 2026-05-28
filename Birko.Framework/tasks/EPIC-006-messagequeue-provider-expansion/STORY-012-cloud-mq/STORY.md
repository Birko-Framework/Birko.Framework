---
id: STORY-012
parent: EPIC-006
status: planned
created: 2026-05-28
---

# Cloud queue providers (Azure Service Bus + AWS SQS)

## User story

As a developer running on Azure or AWS, I want native cloud queue providers so I don't have to manage my own broker.

## Behaviour

- Azure Service Bus: queues, topics, sessions, dead-letter
- AWS SQS: standard + FIFO queues, message groups, delay queues
- Cloud-IAM-based auth supported (managed identity / instance profile)

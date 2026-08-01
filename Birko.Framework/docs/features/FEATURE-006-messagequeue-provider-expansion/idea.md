---
id: FEATURE-006
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.MessageQueue — Provider expansion

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-006](../../../tasks/EPIC-006-messagequeue-provider-expansion/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Add the major broker integrations beyond the existing MQTT / InMemory / Redis Streams providers — RabbitMQ (AMQP), Kafka, Azure Service Bus, AWS SQS, and a MassTransit adapter.

## Proposed shape

- Five sibling shared projects exist and are registered
- Each implements `IMessageQueue` (publish, subscribe, ack/nack, dead-letter where applicable)
- Provider-specific features exposed via options (exchanges, partitions, sessions, FIFO)
- xUnit tests with Testcontainers or SDK mocks

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.MessageQueue.RabbitMQ, Birko.MessageQueue.Kafka, Birko.MessageQueue.Azure, Birko.MessageQueue.Aws, Birko.MessageQueue.MassTransit]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.

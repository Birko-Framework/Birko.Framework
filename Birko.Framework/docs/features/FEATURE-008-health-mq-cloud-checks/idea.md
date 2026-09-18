---
id: FEATURE-008
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Health — Queue + cloud health checks

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-008](../../../tasks/EPIC-008-health-mq-cloud-checks/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Health checks for the message queue providers added in EPIC-006, plus cloud-broker checks that fit naturally in `Birko.Health.Azure` and a new `Birko.Health.Aws`.

## Proposed shape

- `RabbitMqHealthCheck` and `KafkaHealthCheck` in `Birko.Health.Data`
- `AzureServiceBusHealthCheck` in `Birko.Health.Azure`
- `AwsSqsHealthCheck` in `Birko.Health.Aws` (new project, scaffolded as part of this epic)
- Each follows the standard probe pattern (TCP / management API / SDK call) and registers via DI extensions

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Health.Data, Birko.Health.Azure, Birko.Health.Aws]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.

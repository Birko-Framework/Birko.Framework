---
id: FEATURE-012
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.MessageQueue.MQTT — v5 features

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-012](../../../tasks/EPIC-012-mqtt-v5-features/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

MQTT v5 protocol features on top of the existing MQTT v3.1.1 adapter — topic aliases and user properties. Driven by bandwidth optimization for high-frequency IoT sensors where repeated full topic strings cost real money.

## Proposed shape

- `MqttExtensions` exposes topic-alias + user-properties API
- Consumers opt into v5 features when the broker supports them; v3.1.1 path stays default
- Falls back gracefully when v5 capabilities are absent

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.MessageQueue.MQTT]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.

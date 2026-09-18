---
id: FEATURE-010
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Data.RavenDB — Index ergonomics

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-010](../../../tasks/EPIC-010-ravendb-index-ergonomics/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Final remaining RavenDB index management enhancement — attribute-driven index definitions (Option B from the original design). Bulk-deploy and Map/Reduce query helpers are already done.

## Proposed shape

- Decorating a model with index attributes auto-discovers and deploys the index
- Existing `IndexDefinition` / `AbstractIndexCreationTask` paths still work unchanged
- Tests cover discovery + deployment + idempotency

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Data.RavenDB]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.

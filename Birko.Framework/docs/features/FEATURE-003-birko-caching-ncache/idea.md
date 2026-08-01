---
id: FEATURE-003
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Caching.NCache

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-003](../../../tasks/EPIC-003-birko-caching-ncache/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Add NCache as a distributed cache provider for `Birko.Caching` (alongside the existing Memory/Redis/Hybrid implementations).

## Proposed shape

- `Birko.Caching.NCache` sibling project exists, registered in `.slnx` / `.code-workspace` / aggregator
- `NCacheCache` implements `ICache` over `Alachisoft.NCache.Client`
- xUnit + FluentAssertions tests passing

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Caching.NCache]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.

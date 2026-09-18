---
id: FEATURE-011
created: 2026-05-28
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Framework — Test coverage gaps

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-011](../../../tasks/EPIC-011-test-coverage-gaps/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Fill remaining test coverage holes — the Redis-dependent test projects that were deferred for infrastructure availability, the Phase 4 lower-priority surface (Models, ViewModel CRUD, Configuration, Contracts), and the **Birko.Web.\* frontend bucket, which has no unit-test runner at all** (its only automated coverage is the playground's `backport-smoke.ts` via `verify.mjs` — TASK-052).

## Proposed shape

- Redis-dependent test projects ship with Testcontainers-backed runs
- Phase 4 test projects exist with meaningful coverage of their respective domains
- CI green; any remaining intentional gaps are documented

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.BackgroundJobs.Redis.Tests, Birko.Caching.Redis.Tests, Birko.Models, Birko.Data.ViewModel, Birko.Configuration, Birko.Contracts, Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.

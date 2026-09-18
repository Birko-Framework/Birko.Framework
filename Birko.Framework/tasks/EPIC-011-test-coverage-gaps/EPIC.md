---
id: EPIC-011
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.BackgroundJobs.Redis.Tests, Birko.Caching.Redis.Tests, Birko.Models, Birko.Data.ViewModel, Birko.Configuration, Birko.Contracts, Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell]
---

# Birko.Framework — Test coverage gaps

## Area of concern

Fill remaining test coverage holes — the Redis-dependent test projects that were deferred for infrastructure availability, the Phase 4 lower-priority surface (Models, ViewModel CRUD, Configuration, Contracts), and the **Birko.Web.\* frontend bucket, which has no unit-test runner at all** (its only automated coverage is the playground's `backport-smoke.ts` via `verify.mjs` — TASK-052).

## Success criteria

- Redis-dependent test projects ship with Testcontainers-backed runs
- Phase 4 test projects exist with meaningful coverage of their respective domains
- CI green; any remaining intentional gaps are documented

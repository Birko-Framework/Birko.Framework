---
id: EPIC-011
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.BackgroundJobs.Redis.Tests, Birko.Caching.Redis.Tests, Birko.Models, Birko.Data.ViewModel, Birko.Configuration, Birko.Contracts]
---

# Birko.Framework — Test coverage gaps

## Area of concern

Fill remaining test coverage holes — the Redis-dependent test projects that were deferred for infrastructure availability, plus the Phase 4 lower-priority surface (Models, ViewModel CRUD, Configuration, Contracts).

## Success criteria

- Redis-dependent test projects ship with Testcontainers-backed runs
- Phase 4 test projects exist with meaningful coverage of their respective domains
- CI green; any remaining intentional gaps are documented

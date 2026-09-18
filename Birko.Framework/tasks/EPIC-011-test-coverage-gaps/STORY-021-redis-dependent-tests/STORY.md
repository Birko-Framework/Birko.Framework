---
id: STORY-021
parent: EPIC-011
status: planned
created: 2026-05-28
---

# Redis-dependent tests

## User story

As a maintainer, I want the deferred Redis-backed test projects to ship so coverage doesn't have unexplained holes.

## Behaviour

- Tests run against a real Redis instance via Testcontainers
- The original "deferred — needs Redis infra" reason is satisfied
- Covers the same surface area as the other backend test projects (CRUD, expiration, bulk)

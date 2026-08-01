---
id: TASK-117
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P1
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H006]
---

# `RedisCache.ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set

## Context

`../Birko.Caching.Redis/RedisCache.cs:187` — **CONFIRMED**, read verbatim.

With no `KeyPrefix`, `ClearAsync` takes the else branch to
`server.FlushDatabaseAsync(_settings.Database)` — which destroys **every key in that logical database**, not
just this cache's entries.

`RedisSettings.KeyPrefix` **defaults to null**, so this is the default path, not an edge case. And the keys
it destroys belong to siblings that share the connection by design: `Birko.MessageQueue.Redis`,
`Birko.BackgroundJobs.Redis`, and Redis sync stores. A cache clear silently drops queued messages and
pending background jobs.

`ICache.ClearAsync` is documented as *"Removes all entries from the cache"* — the cache, not the database.
The implementation is wider than its own contract.

## Approach

`ClearAsync` must only ever remove keys this cache owns. The options, in preference order:

1. **Require a prefix for `ClearAsync` to be meaningful** — track an owned-key namespace and delete by it
   (`SCAN` + `UNLINK` in batches, not `KEYS`, which blocks the server). Works with no configuration change
   and stops being destructive by default.
2. **Default `KeyPrefix` to something non-null**, so the prefixed branch is always taken. Simpler, but it
   changes key layout for every existing consumer — a silent cache miss on deploy, and worse if a consumer
   reads those keys from elsewhere.
3. Throw when `ClearAsync` is called with no prefix. Honest, but breaks a working (if dangerous) call.

Prefer (1). Whatever is chosen, **`FLUSHDB` should not be reachable from a cache API**; if a caller genuinely
wants to flush a database, that deserves an explicitly named method that says so.

Check the other Redis-backed projects for the same pattern before fixing — a `FLUSHDB` behind a
component-scoped "clear" is the kind of thing that gets copied, and this is a family with four Redis
backends.

## Acceptance criteria

- [ ] `ClearAsync` with no `KeyPrefix` removes this cache's entries and **leaves other keys in the same
      database intact** — asserted with a foreign key written before the clear
- [ ] `ClearAsync` with a `KeyPrefix` is unchanged
- [ ] Keys belonging to `Birko.MessageQueue.Redis` / `Birko.BackgroundJobs.Redis` survive a cache clear
- [ ] `FLUSHDB` is not reachable from `ICache`; if a flush API is kept, it is separately named and documented
- [ ] Key enumeration uses `SCAN`, not `KEYS`, and deletes in batches
- [ ] The other Redis-backed projects are checked for the same pattern, and any hit is filed or fixed
- [ ] Regression tests in `Birko.Caching.Redis.Tests`. If a live server is required, follow the STORY-042
      Docker-gated tier convention rather than skipping the assertion
- [ ] `/specs regen` for `caching`, spec diff reviewed

## Out of scope

- `SH-H004` / `SH-H005` (cache keys derived from `Expression.ToString()` colliding across tenants; SQL cache
  keys carrying no database/connection/tenant identity) — same area, unverified, and **arguably higher
  impact than this one**. Verify them next.
- `SH-H007` (`UpdateAsync(filter, Action<T>)` writing back a stale cached snapshot).

## Human test plan

N/A — covered by automated tests, provided the foreign-key-survives assertion runs against a real or faked
Redis. If it can only be asserted against a live server, that gate is the test — do not downgrade it to a
mock that cannot observe `FLUSHDB`.

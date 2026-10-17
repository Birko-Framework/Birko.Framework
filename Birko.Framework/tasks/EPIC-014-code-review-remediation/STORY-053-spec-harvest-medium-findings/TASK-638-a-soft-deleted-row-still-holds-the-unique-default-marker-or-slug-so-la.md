---
id: TASK-638
parent: STORY-053
feature: FEATURE-014
# status: done | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A soft-deleted row still holds the unique default marker or slug, so later promotions and slugs are refused

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `store-decorator-composition`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

A consequence of [[TASK-632]]. Soft delete never clears `IDefaultMarker.DefaultMarker`, and the default decorators'
"another default is stored" check reads through SoftDelete, which hides the row. So a soft-deleted former default with
`DefaultMarker = true` under the unique index refuses every new promotion, and the retry cannot help
(`Birko.Data.Patterns/Decorators/DefaultStoreWrapper.cs:145-146`, `AsyncDefaultStoreWrapper.cs:151-153`). Slugs are the
same: a soft-deleted row keeps its slug under the unique index, the probe and `SlugHeld` (both through SoftDelete) say
the slug is free, and the store's refusal propagates (`SluggableStoreWrapper.cs:50,110`, `AsyncSluggableStoreWrapper.cs:55-57`).

The docs' `WhereNull = DeletedAt` scoping covers neither the marker nor MySQL, which refuses that predicate on a unique
index (rule 58).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Decide: soft delete clears the marker (and frees the slug), or the index is scoped to live rows where the provider allows it, or both
- [x] Measured live with SoftDelete composed over Default and Sluggable, on SQLite and one server
- [x] Red on the current code; the spec `docs/specs/store-decorator-composition.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced live on all four SQL providers, 8 of 8 red.** The chain was composed by hand in `StoreWrapperBuilder`'s
order (Default → Sluggable → SoftDelete → raw). Create a default `"Widget"`, soft-delete it, then create another
default `"Widget"`. SQLite, PostgreSQL, MySQL and SQL Server all refused both the promotion and the slug with
`StoreUniqueViolationException`. The deleted row kept `DefaultMarker = true` and `Slug = "widget"` under the model's
unique indexes, and every probe read through SoftDelete, so it answered "free". The retries then rethrew, because
`OtherDefaultStored` / `SlugHeld` could not see the holder either.

**Decision (user, 2026-10-17): the two halves are answered differently.**
- *Marker: soft delete retires the default.* Every delete path (entity, collection, filter; sync and async) sets
  `IsDefault = false` and an `IDefaultMarker`'s `DefaultMarker = null` in the same write, through
  `SoftDeleteFilter.Stamp` / `Deletion`. A deleted row is invisible, so it cannot serve as the default anyway, and this
  works on MySQL too. Scoping the index with `WhereNull = DeletedAt` was rejected: MySQL refuses that declaration
  (rule 58), and the deleted row would still read `IsDefault = true` underneath.
- *Slug: it stays reserved until a hard delete.* The Sluggable probe (`SlugProbe`, used by `ResolveSlug` and
  `SlugHeld`) asks the store beneath an `ISoftDeleteStoreWrapper` directly inside it. The new row gets `widget-2`, the
  answer is the same on every provider, and an old URL never starts pointing at a different item. Only one hop, so a
  Tenant decorator below SoftDelete still scopes the probe. This reverses the builder's stated intent ("uniqueness
  checks only consider non-deleted records"), which no unique index could enforce. A live-row `WhereNull` scope is
  refused on MySQL, and rewriting the slug on delete would mutate stored data.

**Red check, by neutralising each half on its own.** Marker retirement off: the 8 live default tests (soft delete by
entity and by filter, on 4 providers) and the 3 unit default tests went red, while the slug tests stayed green. Slug
probe off: the 4 live and 2 unit slug tests went red. Tests: `Task638SoftDeletedUniqueLiveTests` (Migrations.SQL,
12 live) and `Task638SoftDeletedUniqueTests` (Patterns, 6, InMemory, every delete path).

**Docs:** `docs/patterns.md` (the Soft Delete and Sluggable sections; the `WhereNull = DeletedAt` advice for slug
indexes removed), the spec `store-decorator-composition` (ordering exception, slug scenario inverted, a new
retirement scenario), `Birko.Data.Patterns/CLAUDE.md`, and the builder comment. In passing, the `-1, -2` suffix example
was corrected: the first duplicate is `-2`.

**Regression:** the 88 suites that import `Birko.Data.Patterns` or `Birko.Data.Composition` (4,899 tests) are green.
They ran live against PostgreSQL, MySQL, SQL Server, TimescaleDB, MongoDB, RavenDB, ElasticSearch, Cosmos DB (Gateway)
and InfluxDB on local Docker. There was no Redis server, so the Redis-gated live tests in `Birko.Caching.Redis.Tests`
and `Birko.BackgroundJobs.Redis.Tests` returned early.

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-638` — leave empty until then._

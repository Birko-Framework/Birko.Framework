---
id: TASK-632
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M327, SH-M328]
pr: null
github-issue: null
jira-key: null
---

# Concurrent writers defeat the unique-slug and single-default decorators, which only read then write

## Context

Spawned by [[TASK-159]] (2026-10-15), which confirmed both findings by reading. TASK-159 fixed the single-caller defects
of these decorators (the slug probe, the batch's own slugs, the default rule on filtered and property updates); what is
left is the race, and no decorator can close it.

- **SH-M327:** the slug decorators probe `Slug == s` and then write. Two callers creating "widget" at once both see it
  free and both store it.
- **SH-M328:** the default decorators read the defaults, unset them, then write their own. Two callers promoting two rows
  at once both clear the same set and both store a default.

A read-then-write in a decorator narrows the window and cannot close it: the invariant needs the storage to enforce it.

## What to decide

1. **Slug:** a unique index on the slug column, declared by the model (`[UniqueField]` / `[CompositeIndex]` with a tenant
   column where the model is tenant-scoped), and the decorator turning the duplicate-key error into a retry with the next
   suffix. Soft-deletable models need it scoped to live rows: `WhereNull = DeletedAt` (rule 58, TASK-273) expresses that.
2. **Default:** "at most one row with `IsDefault = true`" is a partial unique index over a **boolean value**, which
   `WhereNotNull` / `WhereNull` cannot express (rule 58 deferred the general predicate as an uncontainable sink). Options:
   a nullable marker column (`DefaultMarker` = 1 or null) under `WhereNotNull`, a provider-native constraint, or an
   explicit "best effort" contract on the decorator. Measure each provider before choosing (rule 58: each polarity is
   answered separately).
3. Non-SQL backends (InMemory, JSON, XML, Mongo, Raven, ES, Cosmos): which can enforce, and what the decorator documents
   where none can.

## Decisions (2026-10-16, by the user)

1. **Default: a marker column.** A model opts in with a nullable marker (`true` or null, never `false`) that the
   decorator keeps in step with `IsDefault`, under a unique index (`WhereNotNull` on SQL Server, droppable on MySQL by
   rule 58). *Rejected:* a new `WhereTrue` index predicate (MySQL has no partial index, so it would stay unprotected)
   and best effort only.
2. **On a storage refusal the decorators retry, bounded.** Slug: probe again and take the next suffix. Default: unset
   the others and write again, so the last writer wins. Then the error surfaces. A unique violation on update becomes
   recognisable through a `StoreException` subclass, so existing catches keep working. *Rejected:* surfacing the error.
3. **Backends that cannot enforce are documented as best effort** (ElasticSearch, RavenDB, Cosmos DB, JSON, XML,
   InMemory): the read-then-write narrows the window and does not close it. *Rejected:* an in-process lock (process-wide
   shared state, rules 36/62) and refusing to wrap them.

## Acceptance criteria

- [x] Two concurrent creates of one slug source end with distinct slugs or one refusal, never two equal slugs, measured
      on SQLite and one server
- [x] Two concurrent promotions end with one default, measured the same way
- [x] Where a backend cannot enforce, the decorator says so in its contract
- [x] Red on the current code

## Resolution (2026-10-16)

**Both races closed where the storage enforces a unique index, measured live on SQLite, PostgreSQL, MySQL and SQL Server**,
with the user's three decisions above.

- **Exception.** `StoreUniqueViolationException : StoreException` is the one type for a unique refusal:
  `StoreException.DuplicateCreate` returns it, and the new `StoreException.UniqueViolation` covers an update. The abstract
  stores' public updates translate `IsDuplicateKeyException` (MongoDB, Cosmos DB), and the SQL stores now classify through
  the connector (`IsUniqueViolationException`), single and collection. Rule 9 enumerated first: the only selector on a
  unique violation is `SqlEventStore`, which goes through the connector directly and is untouched.
- **Slug** (four wrappers). A refusal is retried, up to `MaxSlugAttempts` = 5: the wrapper probes again and takes the next
  suffix. It surfaces at once when the slug is free, because the collision was another key. A batch create is retried
  only while none of the batch is stored.
- **Default** (two wrappers). New opt-in `IDefaultMarker` (`bool? DefaultMarker`, true or null, never false). The wrappers
  keep it in step, retry a refused promotion (unset the winner, write again: last writer wins, `MaxDefaultAttempts` = 5),
  and write demotions before the promotion in a batch. Filtered updates go through the same collection path.
- **Wider than filed:** RavenDB, InMemory, JSON and XML hand-built their duplicate-create refusals as plain
  `StoreException`, so "one type on every provider" (TASK-625) had four exceptions. All four now throw the subclass.
- **Measured on the way:** on MongoDB a unique *sparse* index over the marker refuses the second non-default (the null is
  stored, E11000); a partial index (`{ DefaultMarker: true }`) is right, and Birko's index manager cannot declare one.
  Filed as [[TASK-636]]; the docs tell MongoDB users to create it directly.

**Red-verified, as numbers.** With the decorators reverted to HEAD: the live race tests failed **8 of 8** (every slug race
refused the loser, every promotion race left two defaults); the InMemory retry tests failed **4 of 6** (the other two are
pins: a refusal for another key surfaces, and a batch demotion lands first, both true of the old code too, which never set
the marker). With the four providers' refusals reverted, **7 of 7** tightened assertions failed.

**Regression:** the 122 suites importing Birko.Data.Core, Birko.Data.Stores, Birko.Data.SQL or Birko.Data.Patterns
(6,830 tests), live: two failures, one the MySQL `TASK295_…` flake (190/190 on rerun) and one the RavenDB assertion that
led to the provider fix above. Then the 32 suites importing InMemory, JSON, XML or RavenDB (1,450 tests), green. Two
exact-type assertions on a duplicate create (`StoreException`) now name the subclass.

## Human test plan

N/A: a library contract, asserted by tests.

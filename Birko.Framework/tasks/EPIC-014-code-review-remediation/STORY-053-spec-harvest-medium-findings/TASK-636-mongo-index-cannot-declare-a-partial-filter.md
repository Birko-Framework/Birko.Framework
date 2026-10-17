---
id: TASK-636
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P3
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A MongoDB index cannot be declared partial, so `IDefaultMarker`'s unique index must be created by hand

## Context

Found while fixing [[TASK-632]] (2026-10-16). "At most one default" is enforced by a unique index over
`IDefaultMarker.DefaultMarker` (`true` on the default row, null elsewhere). On SQL the model declares it with
`[IndexedField(..., IsUnique: true, WhereNotNull = …)]`. On MongoDB it cannot be declared through Birko at all:

- **Sparse is the wrong tool, measured** (mongosh against the live `birko-live-mongo` container): Birko writes null values explicitly
  (no `IgnoreIfNull` convention), and a unique *sparse* index indexes an explicit null, so the second non-default row is
  refused with `E11000`.
- **A partial index is the right one, measured:** `partialFilterExpression: { DefaultMarker: true }` accepted two nulls and
  refused a second `true` (`E11000`).
- `Birko.Data.Patterns.IndexManagement.IndexDefinition` has `Unique` and `Sparse` but no partial filter, so
  `MongoDBIndexManager` cannot create it, and `docs/patterns.md` tells MongoDB users to create the index directly.

The same gap blocks a soft-delete-scoped unique slug on MongoDB (`DeletedAt` null, the opposite polarity).

## What to decide

1. **The shape.** Mirror SQL's two closed polarities (rule 58): `WhereNotNull` / `WhereNull` column lists, plus the
   boolean case `DefaultMarker: true` needs. A general filter document would be caller text reaching the server, so a
   closed shape is preferable (rule 45). Note `WhereNotNull` on Mongo must render as `{ $exists: true, $ne: null }` or
   `{ $type: … }`. Partial filters accept only a restricted operator set (`$exists: true`, equality, `$gt`/`$lt`, `$type`,
   `$and`), so `$ne: null` is not available. Measure what renders.
2. **Other index managers** (RavenDB, Cosmos DB, ElasticSearch) have no partial unique index: refuse the declaration
   (rule 41), as TASK-628 refused `Unique` on Cosmos.
3. **Drift:** `IndexDeclarationDriftDetector` ([[TASK-635]]) must compare the predicate too, or a partial and a full index
   read as equal.

## Acceptance criteria

- [x] A shape decided and recorded, with reasons
- [x] `MongoDBIndexManager` creates, reports (`GetInfoAsync` / `ListAsync`) and drops a partial unique index; live, two
      null markers accepted and a second `true` refused
- [x] The other index managers refuse a predicate they cannot express
- [x] Drift detection compares the predicate
- [x] `docs/patterns.md` § Concurrent writers shows the declaration instead of "create it directly"
- [x] Red on the current code

## Resolution (2026-10-17)

**Shape decided, with reasons: two closed field lists, `IndexDefinition.WhereNotNull` / `WhereNull`.** These are SQL's own
polarities (rule 58), not a filter document, which would be caller text reaching the server (rule 45). Measured on
MongoDB 7 first:
- A `partialFilterExpression` accepts equality (`true`, `null`), `$type` (one type or a list), `$exists: true` and `$and`.
- It refuses `$exists: false`, `$ne: null` and `$not`.
- Birko stores nulls explicitly, so `$exists: true` would index them.

So `WhereNotNull` renders as `{ f: { $type: <every non-null BSON type> } }`, and `WhereNull` as `{ f: null }`, which
also matches an absent field. There are several terms under one `$and`. The boolean "`DefaultMarker: true`" case
needs nothing of its own: the marker is never `false`, so `WhereNotNull = { "DefaultMarker" }` is it. Both were
measured as unique partial indexes: two null markers and a missing one were accepted and a second `true` refused, and a
deleted duplicate slug was accepted while a second live one, or one without `DeletedAt`, was refused.

**Implementation:**
- `MongoDBIndexManager.CreateAsync` sends the filter (`PartialFilter`), and refuses `Sparse` with a predicate, a
  combination MongoDB itself forbids.
- `ParseIndexDocument` reads the filter back into the two lists. Anything else is kept verbatim as
  `IndexInfo.UnrecognizedPredicate`.
- `SqlIndexManager` carries the lists as TASK-273 predicates (validated identifiers). The connector refuses what it
  cannot express (MySQL), as for an attribute.
- RavenDB, Cosmos DB and ElasticSearch refuse a predicate (`IndexDefinition.RefusePredicates`, rule 41).
- `RequireCoherentPredicates` refuses an empty field name, or one field in both lists.
- `IndexInfo.WhereNotNull` / `WhereNull` are `null` when a manager does not report predicates; SQL's catalog listing
  does not.
- `IndexDeclarationDriftDetector` gains `PredicateDiffers`. It lists a declared predicate it cannot confirm, or an
  unrecognized stored one, as `Unverified`, never as clean or as drift. Its shape strings render `… WHERE f IS NOT NULL`.

**Red check, by three neutralisations:**
- MongoDB with the partial filter off: 2 live tests failed, because a full unique index indexes the nulls.
- Drift with predicate comparison off: 1 test failed.
- SQL with the predicates dropped: the `WhereNull` test failed. The first SQLite test did NOT go red, because SQLite
  treats NULLs as distinct in a unique index, so a `WhereNotNull` marker index there behaves like a full one. The
  discriminating `WhereNull` test was added for that.

**Tests:** `Task636MongoPartialIndexLiveTests` (4: 2 live, 2 offline), `Task636IndexPredicateTests` (Patterns, 5),
`Task636SqlPartialIndexTests` (SQLite, 2), and refusal tests in RavenDB (2), ElasticSearch (1) and Cosmos DB (1).

**Docs:** `docs/patterns.md` § Concurrent writers shows the declaration instead of "create it directly". Also the spec
`schema-index-and-ddl` (IndexDefinition and the SQL mapping), `Birko.Data.Patterns/CLAUDE.md`, and Recent Updates.

**Regression:** the 88 suites that import `Birko.Data.Patterns` or `Birko.Data.Composition` ran live, with 4,999
passed and 1 failed. The failure was TASK-276's guard `SqlitePoolIsolationTests`: this task's new SQLite test cleared
the connection pools process-wide in `Dispose`, which can break a parallel sibling (rule 61). It now clears only its own
pool, and `Birko.Data.SQL.SqLite.Tests` is 578 of 578.

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-636` — leave empty until then._

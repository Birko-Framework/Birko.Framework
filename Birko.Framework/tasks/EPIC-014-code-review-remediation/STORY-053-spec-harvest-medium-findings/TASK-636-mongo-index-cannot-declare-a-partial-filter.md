---
id: TASK-636
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

- [ ] A shape decided and recorded, with reasons
- [ ] `MongoDBIndexManager` creates, reports (`GetInfoAsync` / `ListAsync`) and drops a partial unique index; live, two
      null markers accepted and a second `true` refused
- [ ] The other index managers refuse a predicate they cannot express
- [ ] Drift detection compares the predicate
- [ ] `docs/patterns.md` § Concurrent writers shows the declaration instead of "create it directly"
- [ ] Red on the current code

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-636` — leave empty until then._

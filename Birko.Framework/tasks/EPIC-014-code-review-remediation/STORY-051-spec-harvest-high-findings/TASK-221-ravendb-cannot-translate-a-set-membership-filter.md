---
id: TASK-221
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-218, TASK-220]
pr: null
github-issue: null
jira-key: null
findings: []
---

# RavenDB cannot translate **any** set-membership filter — `Contains` is unsupported in every spelling

## Context

Found by [[TASK-218]]'s follow-up audit (2026-08-16) while measuring whether the span-binding rewrite
needed wiring into RavenDB. It does **not** — and the reason is a larger defect.

Measured offline (no server: `DocumentStore.Initialize()` and query *building* are local; only execution
needs a database), driver as referenced by `Birko.Data.RavenDB`:

| shape | RQL |
|---|---|
| `x => x.Amount > 3` (baseline) | `from 'Docs' where Amount > $p0` |
| `int[] arr; x => arr.Contains(x.Amount)` | **`NotSupportedException: Expression type not supported: TypedParameterExpression`** |
| `List<int> list; x => list.Contains(x.Amount)` | **same exception** |
| `x => Enumerable.Contains(arr, x.Amount)` | **same exception** |
| `x => x.Amount.In(arr)` (Raven's own operator) | `from 'Docs' where Amount in ($p0)` |

The baseline proves the probe and the RQL-rendering path are sound. **Every** collection `Contains`
fails, regardless of collection type — so unlike MongoDB and CosmosDB this is not the .NET 9+
`MemoryExtensions` binding, and [[TASK-218]]'s `SpanContains.Rewrite` would change one failure into an
identical one. Raven requires `RavenQueryableExtensions.In`.

**Why this matters more than it looks.** `IN` is the canonical N+1 batch pattern — load N entities by a
set of ids in one round trip. A consumer writing the portable spelling that works on SQL, ElasticSearch,
MongoDB and (after [[TASK-220]]) CosmosDB gets a runtime exception on RavenDB alone. It is loud, so no
data is at risk; the cost is that the store is not substitutable, which is the whole point of the store
abstraction.

**Not caught until now because** `Birko.Data.RavenDB.Tests` has no filter-matrix suite at all — the
MongoDB and Cosmos equivalents (`MongoFilterMatrixLiveTests`, `CosmosFilterMatrixLiveTests`) have no Raven
counterpart. That absence is arguably the more important finding.

## Approach

Not settled — there is a real choice, and it should be made deliberately:

1. **Rewrite `Contains` → `In` for RavenDB**, the way [[TASK-218]] rewrites the span binding for MongoDB
   and CosmosDB: a per-backend pre-pass in `Birko.Data.RavenDB` turning
   `collection.Contains(x.Member)` into `x.Member.In(collection)`. Makes the portable spelling work and
   keeps the store substitutable. Needs care with the negated form and with `x.ArrayMember.Contains(const)`,
   which is the *opposite* direction of membership and must not be rewritten.
2. **Document it** in `Birko.Data.RavenDB/CLAUDE.md` and let it throw. Cheap and honest, but leaves the
   abstraction leaky for the one operation most likely to be written.

Recommendation: option 1, because the framework already accepts this shape of fix (`SpanContains`) and
because a store that cannot express `IN` is not really interchangeable with the others.

**Whichever is chosen, add the missing matrix suite** — a `RavenFilterMatrixLiveTests` mirroring the
MongoDB/Cosmos ones. Without it the next divergence is invisible again.

## Acceptance criteria

- [ ] `x => ids.Contains(x.Member)` either translates on RavenDB, or throws an exception that **names
      `.In()` as the supported spelling** rather than `Expression type not supported: TypedParameterExpression`
- [ ] `x => x.Member.In(ids)` keeps working, and the negated set-membership form is covered either way
- [ ] A **non-gated** test pins the chosen behaviour — RQL rendering needs no server, as the measurement above shows
- [ ] `Birko.Data.RavenDB.Tests` gains a filter-matrix suite mirroring `MongoFilterMatrixLiveTests`, so a
      future divergence in any shape is visible rather than unmeasured
- [ ] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- [[TASK-218]] / [[TASK-220]]'s span-binding rewrite. Measured **not** to apply here: all spellings fail
  equally on Raven, so that rewrite would change nothing.

## Human test plan

- [ ] Against a live RavenDB (`docker run -p 8080:8080 ravendb/ravendb`), read a set of entities by id
      using the portable `Contains` spelling and confirm the same rows come back as `.In()` returns.

## Implementation plan

_Populated by `/tasks plan TASK-221` — leave empty until then._

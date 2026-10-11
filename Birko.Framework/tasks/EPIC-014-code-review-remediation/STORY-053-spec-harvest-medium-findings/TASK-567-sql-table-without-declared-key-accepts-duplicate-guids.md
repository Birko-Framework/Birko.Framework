---
id: TASK-567
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-11
depends-on: []
blocks: []
findings: [FIELD-029]
pr: null
github-issue: null
jira-key: null
---

# A SQL table for a model with no declared key accepts a second row with the same Guid — create never refuses it

## Context

Spawned from [[TASK-556]] (2026-10-11), which made bulk create keep a caller-supplied Guid on every provider (CR-L190
reversed). Measured on SQLite with a model that declares no `[PrimaryField]` — the ordinary `AbstractModel`:

```
store.Create(new Row { Guid = g, Name = "first" });       // ok
store.Create(new Row { Guid = g, Name = "single-dup" });  // no throw
store.Create(new[] { new Row { Guid = g, Name = "bulk-dup" } }); // no throw
store.Read(x => x.Guid == g)  → 3 rows: first, single-dup, bulk-dup
```

The generated table is `CREATE TABLE "Rows" (Name TEXT, Guid TEXT)` — **no primary key and no unique constraint on
`Guid`**, although every read, update and delete treats `Guid` as the identity (`DataBase.GetPrimaryFields` falls back to
it). So a create of a Guid that is already stored silently duplicates the row, and later single-row operations act on an
ambiguous key. The in-memory store refuses it (`StoreException`, SH-M310 / [[TASK-163]]); MongoDB (`_id`), Cosmos (`id`)
and Elasticsearch (`Create` op) reject it natively; RavenDB's `Store` overwrites.

Single create always had this. TASK-556 widened the reach: before it, SQL **bulk** create overwrote every caller Guid, so a
bulk batch could not collide — now it can, exactly like a single create.

## Acceptance criteria

- [ ] Decide how a SQL table gets a unique identity when the model declares no key: `Guid` as `PRIMARY KEY`, or a
      `UNIQUE` constraint on it — and how existing tables are handled (DetectDrift reports, migration documented; never a
      silent ALTER)
- [ ] A create (single and bulk) of a stored Guid is refused on every SQL provider, with a typed exception consistent with
      the in-memory store; RavenDB's overwrite on create is decided and documented in the same pass
- [ ] Tests on the shared store contract (`tests/Shared/StoreContract/`) prove a duplicate create is refused on every
      provider, red-verified
- [ ] CHANGELOG migration entry; `/specs regen store-crud-contract` and `schema-index-and-ddl`

## Out of scope

- Identity assignment itself — done in [[TASK-556]].

## Human test plan

N/A — library code; covered by the shared contract tests.

## Implementation plan

_Populated by `/tasks plan TASK-567` — leave empty until then._

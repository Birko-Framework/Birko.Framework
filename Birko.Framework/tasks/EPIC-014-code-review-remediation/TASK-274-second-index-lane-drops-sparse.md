---
id: TASK-274
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-247, TASK-273]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The second index lane silently drops `Sparse` — `IIndexBuilder.Sparse()` is `=> this` in all six schema builders

## Context — found while planning TASK-273 (spawned from its § Out of scope)

This framework has **two** index lanes, and TASK-273 fixes only the first:

1. **attribute → schema-ensure** — `[IndexedField]` / `[CompositeIndex]` → `DataBase_Table.LoadIndexes` →
   `SQL.Tables.IndexDefinition` → `AbstractConnectorBase.CreateIndexSql`. TASK-273 adds
   `WhereNotNull` / `WhereNull` here.
2. **provider-neutral → `IIndexManager` / migrations** — `Birko.Data.Patterns.IndexManagement.IndexDefinition`
   → `SqlIndexManager.ToSqlIndexDefinition` → the same connector emitter, and
   `Patterns.Schema.IIndexBuilder` → each backend's schema builder.

Lane 2 already has a word for "index only some rows": `IndexDefinition.Sparse`, documented as *"skips
documents without the indexed field. Applicable to MongoDB. Ignored by providers that don't support sparse
indexes."* **Measured, 2026-08-22:**

- `IIndexBuilder.Sparse()` is `=> this` — a silent no-op — in **all six** schema builders:
  `SqlSchemaBuilder.cs:333`, `MongoSchemaBuilder.cs:131`, `CosmosDBSchemaBuilder.cs:206`,
  `ElasticSearchSchemaBuilder.cs:135`, `InfluxDBSchemaBuilder.cs:133`, `RavenDBSchemaBuilder.cs:132`.
  So a migration calling `.Sparse()` gets a **full** index on every backend — including MongoDB, whose
  driver *does* support it and whose `MongoDBIndexManager` honours `definition.Sparse` when reached through
  the other door (`MongoDBIndexManager.cs:69`).
- `SqlIndexManager.ToSqlIndexDefinition` copies `Name`, `Unique` and the columns and **drops** `Sparse`
  (and `ExpireAfter`, and `Properties`).
- `IIndexBuilder.WithProperty(key, value)` is a second `=> this` on the same interface, so provider-specific
  index options declared in a migration are discarded too.

**Why this is the same defect twice already fixed next door.** TASK-245 found `ToSqlIndexDefinition`
dropping `Unique` — which is what forced a duplicate `CreateUniqueIndexSql` emitter to exist in three
classes. TASK-246 found `SqlIndexBuilder.Build()` dropping `.Unique()` one layer over, so a migration's
declared UNIQUE constraint was simply absent, silently accepting the duplicate rows the migration existed to
forbid. This is the third instance of the identical lost-flag shape, on the property beside the one that was
fixed both times.

**What makes it worse than an ignored hint.** A dropped `Unique` under-enforces; a dropped `Sparse` can
**over**-enforce. TASK-273 measured that a plain unique index rejects a row whose duplicate is soft-deleted,
on all four SQL providers — so a migration asking for a sparse unique index and silently getting a full one
gets a *stricter* constraint than it declared, which refuses legitimate rows.

⚠ **`Sparse` on lane 2 and `WhereNotNull` on lane 1 are not the same rule, and conflating them is the trap.**
Mongo's sparse on a *compound* index includes a document when **any** key is present; TASK-273's SQL tail
requires **every** named column to be non-null. Decide what `Sparse` means for a SQL backend before wiring
it — mapping it onto TASK-273's machinery is the obvious move and it needs stating, not assuming.

## Acceptance criteria

- [ ] Decide, and record here, what `IndexDefinition.Sparse` means per backend — including whether the SQL
      lane expresses it through TASK-273's `Predicates` and, if so, with which semantics (any-key-present vs
      all-named-non-null) and over which columns.
- [ ] `SqlIndexManager.ToSqlIndexDefinition` carries the flag across, or **refuses** — never drops silently.
      Same for `ExpireAfter` and `Properties`: each is honoured, or refused, or documented as deliberately
      ignored with the reason (§ SH-H037).
- [ ] Every `IIndexBuilder.Sparse()` implementation either honours the flag or throws / records — the six
      `=> this` bodies are the defect. A backend that genuinely cannot express it must say so out loud.
- [ ] MongoDB, which *can* honour it, does — through the schema-builder door as well as the index-manager
      door. Its two doors must not disagree (§ *one producer*).
- [ ] `WithProperty` gets the same treatment: honoured, refused, or documented per backend.
- [ ] Tests take the **connector path**, not the raw-SQL fallback. TASK-246 stayed green because every test
      in `Birko.Data.Migrations.SQL.Tests` constructed the builder with `connector == null`, exercising the
      branch nobody ships (TASK-247 has since made the connector required — verify that holds).
- [ ] Mutation-proven per backend: drop the flag hand-off and a test goes red. A revert failing 0 tests means
      the suite tests the wrong branch.
- [ ] ⚠ **If this lane learns a predicate, it must reach the funnel guard — `SqlIndexManager.CreateAsync`
      calls `_connector.CreateIndexSql(scope, sqlIndex)` DIRECTLY** (`SqlIndexManager.cs:65`), bypassing
      `AbstractConnector.RequireExpressiblePredicates`. Harmless today because a Patterns `IndexDefinition`
      carries no predicates, so nothing unexpressible can reach it — which is exactly why it will be missed.
      Found in TASK-273's own review; the consequence of missing it is that on MySQL the emitter backstop
      throws instead, and `InitException` re-wraps that into a bare `Exception` no
      `catch (InvalidOperationException)` can select.

## Out of scope

- Lane 1 (`[IndexedField]` / `[CompositeIndex]` → schema-ensure) — TASK-273 owns it.
- Emulating a partial unique index on MySQL via a functional key part — measured available, declined and
  recorded in TASK-273's § Out of scope; if it is ever built it serves both lanes.
- A general predicate string on either lane — deferred in TASK-273 with the injection problem named.

## Human test plan

- [ ] N/A — automated coverage per backend; MongoDB and the SQL providers need live servers, which the
      existing suites already gate on.

## Implementation plan

_Populated by `/tasks plan TASK-274` — leave empty until then._

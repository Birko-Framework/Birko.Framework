---
id: TASK-274
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-245, TASK-246, TASK-247, TASK-273]
findings: []
pr: 1e66d61 (Patterns) · 8973961 (Data.SQL) · d23dbfc (Migrations.SQL) · 8153fe5 (.MongoDB) · fde4343 (.ElasticSearch) · 5ceb50b (.RavenDB) · 86c2d68 (.CosmosDB) · 74c05dd (.InfluxDB)
github-issue: null
jira-key: null
---

# The second index lane dropped `Sparse` in all six builders — and three of them created no index at all

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

- [x] Decide, and record here, what `IndexDefinition.Sparse` means per backend — including whether the SQL
      lane expresses it through TASK-273's `Predicates` and, if so, with which semantics (any-key-present vs
      all-named-non-null) and over which columns.
- [x] `SqlIndexManager.ToSqlIndexDefinition` carries the flag across, or **refuses** — never drops silently.
      Same for `ExpireAfter` and `Properties`: each is honoured, or refused, or documented as deliberately
      ignored with the reason (§ SH-H037).
- [x] Every `IIndexBuilder.Sparse()` implementation either honours the flag or throws / records — the six
      `=> this` bodies are the defect. A backend that genuinely cannot express it must say so out loud.
- [x] MongoDB, which *can* honour it, does — through the schema-builder door as well as the index-manager
      door. Its two doors must not disagree (§ *one producer*).
- [x] `WithProperty` gets the same treatment: honoured, refused, or documented per backend.
- [x] Tests take the **connector path**, not the raw-SQL fallback. TASK-246 stayed green because every test
      in `Birko.Data.Migrations.SQL.Tests` constructed the builder with `connector == null`, exercising the
      branch nobody ships (TASK-247 has since made the connector required — verify that holds).
- [x] Mutation-proven per backend: drop the flag hand-off and a test goes red. A revert failing 0 tests means
      the suite tests the wrong branch.
- [x] ⚠ **If this lane learns a predicate, it must reach the funnel guard — `SqlIndexManager.CreateAsync`
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

---

## Closed 2026-08-23

**The filed defect was the smallest part of it.** "Sparse is `=> this` in six builders" was true. Reading
every implementation rather than the one the title named found that the **ElasticSearch, RavenDB and CosmosDB
builders had no `Build()` override at all** — they inherited `IIndexBuilder.Build()`'s no-op default while
accumulating fields, a `Unique()` flag and a live client. On those three backends a migration's
`CreateIndex(...).WithField(...).Unique().Build()` created **nothing**, silently. That is TASK-246's
lost-flag defect, total rather than partial, three backends over.

### The decision, per backend

| Backend | `Sparse()` | `WithProperty()` | `Build()` |
|---|---|---|---|
| **MongoDB** | **honoured** — `CreateIndexOptions.Sparse`, so its two doors now agree | refused (index creation reads only name/unique/sparse/TTL) | unchanged, creates the index |
| **SQL** | **honoured** for one column as TASK-273's `WhereNotNull` predicate; **refused** for a compound index | refused (the emitter models name/unique/columns/predicates) | unchanged |
| **ElasticSearch** | refused | refused | **refuses** — no secondary-index concept; the mapping is the index |
| **RavenDB** | refused | refused | **refuses** — a Raven index is a map/reduce definition, not a field list |
| **CosmosDB** | refused | refused | **refuses** — indexing is a container-level policy |
| **InfluxDB** | no-op (already honest) | no-op | **refuses** — no custom indexes exist at all |

Every refusal goes through one producer, `Birko.Data.Patterns.Schema.IndexBuilderSupport`, so it always
names what could not be honoured, the backend's own reason, and the door that does work.

### Why compound `Sparse` is refused rather than translated

Mongo's sparse compound index includes a document when **any** key is present; a SQL partial index over
`a IS NOT NULL AND b IS NOT NULL` requires **all** of them. `IIndexBuilder` does not say which `Sparse()`
means, so single-column is honoured — the two readings coincide there — and compound is refused, with the
message naming `[CompositeIndex(..., WhereNotNull = ...)]` as the way to state the intent explicitly. Picking
one silently is what this whole family of defects is made of.

### `ToSqlIndexDefinition`: carry or refuse, all three fields

`Sparse` → a predicate (or the compound refusal). `ExpireAfter` → refused; SQL has no TTL index and row
expiry is a scheduled delete, a different feature with different failure modes. `Properties` → refused, and
this one matters because it is **not** decorative elsewhere: the ElasticSearch index manager reads
`NumberOfShards`/`NumberOfReplicas` from it and RavenDB's reads `Map`/`Maps`/`Reduce`. Ignoring it on the SQL
lane would make one lane's contract look like another's.

### Criterion 8, and why it was already written down

Expressing `Sparse` as a predicate means this lane now **carries** predicates — and
`SqlIndexManager.CreateAsync` calls `CreateIndexSql` directly, bypassing `CreateIndexes`' guard.
`RequireExpressiblePredicates` moved from `AbstractConnector` to `AbstractConnectorBase` and became public:
one producer, two callers, no second copy to drift, and the refusal still happens before the statement is
built (inside `DoDdlCommand` it would be re-wrapped into a bare `Exception` no caller can select). TASK-273
recorded this bypass as an out-of-scope note the day before, which is why it was not rediscovered the hard
way.

### Verification

**1,704 tests, 0 failed, 0 skipped** across eighteen suites with `BIRKO_REQUIRE_LIVE` set throughout (live
MongoDB 7 as a single-node replica set, SQL Server 2022, PostgreSQL 16.15, MySQL 8.4.11, on-disk SQLite) —
**24 new**, in seven projects. The SQL tests drive a real database through `SqlSchemaBuilder` **with a
connector**, because TASK-246 stayed green for a release when every test in that project passed
`connector == null` and exercised a fallback nothing shipped.

| Mutation | SQL builder | ToSqlIndexDefinition | Mongo builder | ES refusals |
|---|---|---|---|---|
| SQL builder drops `Sparse` again | **2 red** | green | green | green |
| `ToSqlIndexDefinition` drops the three fields again | green | **4 red** | green | green |
| Mongo builder drops `Sparse` again | green | green | **1 red** | green |
| ES builder back to the inherited no-op `Build()` | green | green | green | **1 red** |

Each mutation hits exactly its own lane and nothing else, which is what shows the four fixes are independent
rather than one fix observed four times.

### ⚠ A live suite needs the right server SHAPE

Starting a standalone `mongod` to cover the Mongo half made **five untouched**
`MongoTransactionBoundaryLiveTests` fail with "Standalone servers do not support transactions" — MongoDB
transactions require a replica set, which that suite's own first test states in its failure message.
`--replSet rs0` plus `rs.initiate()` fixed the fixture and all 97 passed. Recorded because "is the container
up?" is not "is it the server these tests describe?", and the five failures would otherwise have been read as
a regression from this change.

### Deliberately not done

- **No index creation implemented for ElasticSearch, RavenDB or CosmosDB.** Each is a genuine feature with
  its own model (a mapping, a map/reduce definition, a container policy) — three features, not this task.
  Refusing is what makes their absence visible; the messages name `Raw()` and, for Raven,
  `IIndexManager.CreateAsync` with `Properties`, which does work today.
- **`ExpireAfter` is not exposed on `IIndexBuilder`.** It is reachable only through the index-manager door,
  so it is not *dropped* by the builder — it is absent from the interface. A different question from this
  task's, and left alone.
- **No change to the four `IIndexManager` implementations.** They already honour what they can; this task was
  about the door that discarded it.

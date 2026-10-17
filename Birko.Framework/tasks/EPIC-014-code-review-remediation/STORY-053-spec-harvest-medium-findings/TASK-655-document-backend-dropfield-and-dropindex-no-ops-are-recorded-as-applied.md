---
id: TASK-655
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-17
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Document-backend DropField and DropIndex do nothing, and the migration is recorded as applied

## Context

Spawned 2026-10-17 from [[TASK-642]], which made InfluxDB's `RenameField` and `DropField` refuse instead of being
silent no-ops (rule 22: a write that cannot be applied never reports success; measured there, the runner recorded the
migration as applied). Read while fixing it: the same shape is on the other document backends, and the spec
`docs/specs/migrations.md` § *Document backends treat schema operations as no-ops* documents it as intended:

| Backend | `DropField` | `DropIndex` |
|---|---|---|
| MongoDB (`Birko.Data.Migrations.MongoDB/Context/MongoDBSchemaBuilder.cs:69`) | no-op ("could $unset") | drops |
| ElasticSearch (`…ElasticSearch/Context/ElasticSearchSchemaBuilder.cs`, `DropField`) | no-op (needs a reindex) | no-op |
| RavenDB (`…RavenDB/Context/RavenDBSchemaBuilder.cs:70`) | no-op ("PatchByQuery") | drops |
| CosmosDB (`…CosmosDB/Context/CosmosDBSchemaBuilder.cs:81`) | excludes the path from indexing; documents keep the field | no-op |

`AddField` as a no-op is truthful on a schema-less store, since the field can be written at once. `DropField` and
`DropIndex` are not: the data or the index the migration asked to remove is still there.

**Read, not measured: reproduce first** (rule 54).

## Acceptance criteria

- [x] Reproduced per backend: after a migration that drops a field, documents still carry it, and the migration is
      recorded
- [x] Decide per backend: implement where the backend can (MongoDB `$unset` via `UpdateMany`, RavenDB
      `PatchByQuery` `delete this.<field>` with TASK-640's `RqlField`/`RqlCollection`), refuse where it cannot
      (ElasticSearch `DropField` without a reindex; `DropIndex` on ElasticSearch and CosmosDB, whose indexes are not
      separate objects). The open question for CosmosDB is whether excluding the path is a drop.
- [x] A migration using a refused operation fails and stays unrecorded; an implemented one removes the field (live)
- [x] Red on the current code; the spec's *no-ops* requirement rewritten

## Out of scope

- `AddField` no-ops: truthful on a schema-less store.
- InfluxDB: done in [[TASK-642]].

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-655` — leave empty until then._

## Resolution (2026-10-17)

**Reproduced live, 7 red on HEAD.** Containers: mongo:7, RavenDB, the Cosmos emulator and ES on 9202.
- MongoDB, RavenDB and ES: after `DropField` every document still carried the field (1 + 1 + 1).
- RavenDB: a non-path field name was accepted, because nothing read it (1).
- ES: `DropIndex` returned without an error (1).
- Cosmos: `DropField` only excluded the path from indexing, so documents kept the field (1), and `DropIndex` left
  `IX_Code` in place (1).
- ES runner test: a migration calling `DropIndex` **succeeded and was recorded** on HEAD. With the fix it fails with
  `MigrationException` and `CurrentVersion` stays 0. The test first passed on HEAD for the wrong reason: the runner
  was not initialised. It now calls `Initialize()`, as TASK-642's does.

**Decided per backend:**

| Backend | `DropField` | `DropIndex` |
|---|---|---|
| MongoDB | implemented: `UpdateMany` `$unset` where the field exists, in the session | already dropped |
| RavenDB | implemented: `PatchByQuery` `delete this.<field>`, waited for, names through TASK-640's `RqlCollection` / `RqlField` | already dropped |
| CosmosDB | implemented: a point patch `Remove` per document, in its partition, as `RenameField` does. Excluding the path **is not a drop**, so the open question is answered no, and the field's included and excluded paths are removed instead. | implemented: through `CosmosDBIndexManager.DropAsync`, the door `CreateIndex` uses since TASK-628 |
| ElasticSearch | implemented, not refused: an update-by-query removes the field from every document's `_source`, so no document carries it or matches a query on it. The mapping keeps the entry until a reindex, and the spec says so. | refused (`NotSupportedException`), as `CreateIndex` is since TASK-274 |
| InfluxDB | refused since TASK-642 | **refused now too**, as `CreateIndex` is. The task left InfluxDB out as done in TASK-642, but that task covered fields only, and the `DropIndex` no-op was this task's defect. |

**Found on the way:**
- A Cosmos query typed `JsonElement` through the SDK's default (Newtonsoft) serializer comes back empty and throws on
  read. `DropField` reads through a stream query parsed with System.Text.Json (`QueryDocuments`), so it does not
  depend on the caller's client.
- `RenameField` has the read dependency: measured, it throws on a plain client and corrupts nothing. Filed as
  [[TASK-659]].

**Tests:**
- `Task655DropFieldLiveTests` (MongoDB, RavenDB, ES) and `Task655DropFieldAndIndexLiveTests` (Cosmos).
- `DropIndex_refuses_as_CreateIndex_does` (InfluxDB, offline).

**Docs:** the spec requirement "Document backends treat schema operations as no-ops" is rewritten as "drop what a
migration asks to drop, or refuse", with a per-backend table and the measured scenarios. The four projects' CLAUDE.md
lines are updated.

**Regression:** all importers of the four migrations projitems pass live, 134 tests, plus InfluxDB's only importer, 45.

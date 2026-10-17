---
id: TASK-655
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

- [ ] Reproduced per backend: after a migration that drops a field, documents still carry it, and the migration is
      recorded
- [ ] Decide per backend: implement where the backend can (MongoDB `$unset` via `UpdateMany`, RavenDB
      `PatchByQuery` `delete this.<field>` with TASK-640's `RqlField`/`RqlCollection`), refuse where it cannot
      (ElasticSearch `DropField` without a reindex; `DropIndex` on ElasticSearch and CosmosDB, whose indexes are not
      separate objects). The open question for CosmosDB is whether excluding the path is a drop.
- [ ] A migration using a refused operation fails and stays unrecorded; an implemented one removes the field (live)
- [ ] Red on the current code; the spec's *no-ops* requirement rewritten

## Out of scope

- `AddField` no-ops: truthful on a schema-less store.
- InfluxDB: done in [[TASK-642]].

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-655` — leave empty until then._

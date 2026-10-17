---
id: TASK-642
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# InfluxDB migration RenameField and DropField do nothing, and the migration is recorded as applied

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `migrations`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`InfluxDBSchemaBuilder.RenameField` and `DropField` are silent no-ops
(`Birko.Data.Migrations.InfluxDB/Context/InfluxDBSchemaBuilder.cs:75`), so a migration using them is recorded as applied
having changed nothing. Its sibling `UpdateDocuments` now refuses (TASK-152); rule 22: a write that cannot be applied
never reports success.

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] Both refuse (or are implemented), with a test that the migration fails and is not recorded
- [x] Red on the current code; the spec `docs/specs/migrations.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced live on InfluxDB 2 (local Docker), 3 of 3 red.** A migration whose `Up` calls `RenameField` or
`DropField`, run through `InfluxMigrationRunner`, came back `Success = true` and was recorded at version 1, having
changed nothing.

**Fix: both refuse.** They are not implementable. Points are immutable, so a rename means rewriting the series, and
the delete API's predicate cannot name a `_field`. So `DropField` and `RenameField` throw `NotSupportedException`,
naming the field and the way out, as `UpdateDocuments` has since TASK-152. The runner now fails the migration with a
`MigrationException`, and `CurrentVersion` stays 0. `AddField` and `DropIndex` stay no-ops, because on a schema-less,
index-less store the state they ask for already holds.

**Spawned [[TASK-655]]:** `DropField` is a no-op on MongoDB, ElasticSearch and RavenDB too (on CosmosDB it only
excludes the indexing path), and `DropIndex` is one on ElasticSearch and CosmosDB. The spec documents those as
intended, so each backend needs its own decision (implement or refuse).

**Tests:** `Task642InfluxFieldOpsLiveTests` (2 live through the runner, 1 offline). The only importer,
`Birko.Data.Migrations.InfluxDB.Tests`, is 44 of 44, live.

**Docs:** the spec `migrations`, `Birko.Data.Migrations.InfluxDB/CLAUDE.md`, and Recent Updates (shared with
TASK-643, the oldest entry rolled).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-642` — leave empty until then._

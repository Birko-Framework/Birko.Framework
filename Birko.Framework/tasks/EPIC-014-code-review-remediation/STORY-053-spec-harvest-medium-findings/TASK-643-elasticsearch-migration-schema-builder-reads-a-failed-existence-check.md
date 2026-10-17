---
id: TASK-643
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

# ElasticSearch migration schema builder reads a failed existence check as absent and ignores the create response

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `migrations`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`ElasticSearchSchemaBuilder.CreateCollection`, `DropCollection` and `CollectionExists`
(`Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchSchemaBuilder.cs:27-50`) read a failed `Indices.Exists` as
"absent" and never check the `Create` response. The SH-M202 pattern TASK-152 fixed in the migration store, not here
(rule 26).

## Acceptance criteria

- [x] Reproduced (or refuted, with the measurement)
- [x] A failed existence check and a failed create throw; tested with a fake transport
- [x] Red on the current code; the spec `docs/specs/migrations.md` updated if behaviour changes

## Resolution (2026-10-17)

**Reproduced, 3 of 4 red.** No server was needed:
- Against an unreachable cluster, `CreateCollection`, `DropCollection` and `CollectionExists` all answered as if the
  index were absent.
- With a fake transport answering HEAD 404 and then 403, `CreateCollection` returned without complaint.
- With HEAD 200 and then 403, `DropCollection` did the same.

The fourth test pins that a real 404 and a real 200 answer as before.

**Fix:** one private `IndexExists` behind all three methods. It reads only a 404 as absent and throws
`InvalidOperationException` for any other failed answer, which is the SH-M202 rule `ElasticSearchMigrationStore`
keeps since TASK-152. The create and delete responses go through `ElasticSearchDataMigrator.EnsureValid`.

**Tests:** `Task643SchemaBuilderFailureTests` (4, with a `RoutedConnection` over NEST's `InMemoryConnection` that
answers by HTTP method). The only importer, `Birko.Data.Migrations.ElasticSearch.Tests`, is 35 of 35, live against
ElasticSearch 7.17.

**Docs:** the spec `migrations` (the collection requirement), `Birko.Data.Migrations.ElasticSearch/CLAUDE.md`, and
Recent Updates (shared with TASK-642).

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-643` — leave empty until then._

---
id: TASK-643
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

- [ ] Reproduced (or refuted, with the measurement)
- [ ] A failed existence check and a failed create throw; tested with a fake transport
- [ ] Red on the current code; the spec `docs/specs/migrations.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-643` — leave empty until then._

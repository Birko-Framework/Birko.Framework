---
id: TASK-640
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# RavenDB migrations put update keys, the collection name and RenameField's fields into RQL unescaped

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `migrations`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`RavenDBDataMigrator` interpolates update keys (`this.{key}`) and the collection (`FROM '{collection}'`) into the RQL
text raw (`Birko.Data.Migrations.RavenDB/Context/RavenDBDataMigrator.cs:32,37,61`); filter fields are validated by
`RqlField` since TASK-152, these are not. `RavenDBSchemaBuilder.RenameField` does the same with the collection and both
field names (`Context/RavenDBSchemaBuilder.cs:80`). The shape of an injection (rules 33, 45).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Every caller name reaching RQL is validated (or passed as a parameter where RQL allows), with a refusal test
- [ ] Red on the current code; the spec `docs/specs/migrations.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-640` — leave empty until then._

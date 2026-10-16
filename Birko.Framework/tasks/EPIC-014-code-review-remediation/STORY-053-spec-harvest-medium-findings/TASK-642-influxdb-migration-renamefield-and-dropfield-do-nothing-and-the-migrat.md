---
id: TASK-642
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

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Both refuse (or are implemented), with a test that the migration fails and is not recorded
- [ ] Red on the current code; the spec `docs/specs/migrations.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-642` — leave empty until then._

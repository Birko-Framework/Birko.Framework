---
id: TASK-639
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

# A filter on a Persistent or Auto SQL view names the source column, which the view does not have

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `views-and-aggregation`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

A consequence of [[TASK-151]] (SH-M405). `SqlViewStore`'s scoped filter resolver returns
`field.GetSelectName(withTableName)`, the source column, usually table-qualified (`Persons.Name`)
(`Birko.Data.SQL.Views/SqlViewStore.cs:121-130`). The persistent read path applies those conditions to
`SELECT … FROM "<view>"` (`Birko.Data.SQL.View/SQL/Connectors/AbstractConnectorBase_View.cs:91`), whose columns are the
view-property aliases. TASK-151's tests covered the filter on OnTheFly only.

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] A filter on a renamed view property works on a Persistent and an Auto view, live on the four SQL providers
- [ ] Red on the current code; the spec `docs/specs/views-and-aggregation.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-639` — leave empty until then._

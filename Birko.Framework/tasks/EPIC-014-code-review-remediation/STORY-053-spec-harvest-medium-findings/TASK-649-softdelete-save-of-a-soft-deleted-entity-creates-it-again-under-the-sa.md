---
id: TASK-649
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

# SoftDelete Save of a soft-deleted entity creates it again under the same Guid

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `store-decorator-composition`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`Save` of a soft-deleted entity: `Update` throws `EntityNotFoundException` (TASK-159), and `StoreSave` then runs
`Create` under the same Guid against a store that still holds the row
(`Birko.Data.Patterns/Decorators/SoftDeleteStoreWrapper.cs:52-54,73`, async twin): a duplicate-key error or a
resurrection, depending on the backend. Also minor: a soft-delete bulk update whose inner update throws for missing items
never reports the soft-deleted ones (`SoftDeleteBulkStoreWrapper.cs:41-46`).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Decide what Save of a soft-deleted entity means (refuse, or restore explicitly) and test it on two backends
- [ ] Red on the current code; the spec `docs/specs/store-decorator-composition.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-649` — leave empty until then._

---
id: TASK-638
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

# A soft-deleted row still holds the unique default marker or slug, so later promotions and slugs are refused

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `store-decorator-composition`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

A consequence of [[TASK-632]]. Soft delete never clears `IDefaultMarker.DefaultMarker`, and the default decorators'
"another default is stored" check reads through SoftDelete, which hides the row. So a soft-deleted former default with
`DefaultMarker = true` under the unique index refuses every new promotion, and the retry cannot help
(`Birko.Data.Patterns/Decorators/DefaultStoreWrapper.cs:145-146`, `AsyncDefaultStoreWrapper.cs:151-153`). Slugs are the
same: a soft-deleted row keeps its slug under the unique index, the probe and `SlugHeld` (both through SoftDelete) say
the slug is free, and the store's refusal propagates (`SluggableStoreWrapper.cs:50,110`, `AsyncSluggableStoreWrapper.cs:55-57`).

The docs' `WhereNull = DeletedAt` scoping covers neither the marker nor MySQL, which refuses that predicate on a unique
index (rule 58).

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Decide: soft delete clears the marker (and frees the slug), or the index is scoped to live rows where the provider allows it, or both
- [ ] Measured live with SoftDelete composed over Default and Sluggable, on SQLite and one server
- [ ] Red on the current code; the spec `docs/specs/store-decorator-composition.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-638` — leave empty until then._

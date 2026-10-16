---
id: TASK-641
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

# A stored grant whose role was later retired makes permission checks for that user and type throw

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `security-and-authorization`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

`GrantService.EffectiveActionsAsync` (`Birko.Security.Grants/Services/GrantService.cs:245`) and `ListResourcesAsync`
(`:56`) throw `GrantConfigurationException` on a grant whose role is no longer in the role table. So one such grant makes
every `CanAsync`, `ListResourcesAsync` and non-admin `AuthorizeAsync` for that user on that resource type throw. Only the
admin path was made tolerant of retired roles.

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Decide what a retired role means for a check (ignore the grant, or refuse loudly at role retirement)
- [ ] Red on the current code; the spec `docs/specs/security-and-authorization.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-641` — leave empty until then._

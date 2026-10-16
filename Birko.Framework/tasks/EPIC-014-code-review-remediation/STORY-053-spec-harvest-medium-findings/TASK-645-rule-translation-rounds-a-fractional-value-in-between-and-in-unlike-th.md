---
id: TASK-645
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

# Rule translation rounds a fractional value in Between and In, unlike the in-memory evaluator

## Context

Found by the batched spec regeneration of 2026-10-16 (17 areas, harvested by reading the code), area `specifications-and-paging`. **A
suspected defect from reading, not a measurement: reproduce it first** (rule 54). If it does not reproduce, close this
as refuted with the measurement.

TASK-171's fractional-against-integral fix (SH-M294) covers the six comparison operators only. `Between` and `In`
still convert with `ChangeType`, which rounds (`Birko.Rules/Expressions/RuleExpressionConverter.cs:286-289,337`), so
`Qty Between 10.5..20` matches 10 and `Qty In [1.5]` matches 2. The in-memory evaluator compares exactly
(`ComparisonHelper.cs:28,204`), so the two engines disagree, which [[TASK-144]] set out to end.

## Acceptance criteria

- [ ] Reproduced (or refuted, with the measurement)
- [ ] Between and In agree with the evaluator for fractional values against integral members
- [ ] Red on the current code; the spec `docs/specs/specifications-and-paging.md` updated if behaviour changes

## Human test plan

N/A: a library contract, asserted by tests.

## Implementation plan

_Populated by `/tasks plan TASK-645` — leave empty until then._

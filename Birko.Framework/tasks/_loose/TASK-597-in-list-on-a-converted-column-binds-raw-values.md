---
id: TASK-597
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
picked-by: fix-next
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-035]
pr: null
github-issue: null
jira-key: null
---

# An `IN` list (`values.Contains(x.Col)`) on a converted SQL column may still bind raw values

## Context

Spawned from [[TASK-596]]'s outcome, 2026-10-14. TASK-596 made a comparison's value (`x.At == instant`, `<`, `>`, …)
bind through the column field's `ToStoredValue`, in `DataBase.StoreComparedValues`. A `Contains` call that becomes
`IN (…)` (`instants.Contains(x.At)`) is built by a different branch of `ParseConditionExpression`, the method-call
branch, and was not touched. On a `[UtcField]` column, an `IN` list of `DateTime`s would then compare offset-less
text against stored offset text and match nothing, the same defect TASK-596 fixed for `==`. **Not measured.**

## Acceptance criteria

- [ ] Measured on SQLite first (rule 54): `instants.Contains(x.At)` on a `[UtcField]` column, against entity-written
      rows. The finding is confirmed or falsified.
- [ ] If confirmed: the `IN` values go through the field's `ToStoredValue`, the same single producer as TASK-596. An
      empty list keeps meaning "matches nothing".
- [ ] A regression test fails with the fix removed.

## Out of scope

- Other converted kinds than `[UtcField]`. TASK-596 measured `TimeOnly`, enums, `decimal` and plain `DateTime` as
  storing their CLR value on SQLite, so an `IN` over them is unaffected there.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-597` — leave empty until then._

## Progress log

- step 2 — picked; the only defect the user named (same root cause as TASK-596, the Contains/IN branch)

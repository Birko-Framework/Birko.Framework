---
id: TASK-882
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-864]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Rule converters: remaining disagreements after TASK-864

## Context

Found by TASK-864 (2026-11-09), not measured in its contract:
- **Non-nullable column against a null value:** `RuleExpressionConverter` degrades to "matches nothing"; the SQL
  converter gives IS NOT NULL (every row) for `NotEqual null` and every row for a negated ordering against null (C#'s
  lifted answer). `Amount In [null, 5]` matches nothing in memory and matches 5 in SQL.
- **Like with an interior `%`** (`a%b`) is unevaluable in memory but runs in SQL (related to SH-M362).
- **Rule values reach SQL without `ToStoredValue`:** an enum or Guid sent as text is not converted to the stored form,
  unlike the lambda path.

## Acceptance criteria

- [ ] Reproduce each (rule 54) against C#, in memory and in SQL.
- [ ] Each one that differs is made consistent (one answer per rule, wherever it runs) or refused by name.
- [ ] Tests, red before the fix.

## Out of scope

- NULL on nullable columns (TASK-864, done); `RuleEvaluator` NULL ordering (TASK-881).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

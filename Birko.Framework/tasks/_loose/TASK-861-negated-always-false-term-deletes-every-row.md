---
id: TASK-861
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-859]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A negated group around an always-false term deletes every row

## Context

Found by TASK-859 (2026-11-07), measured on SQLite. It predates TASK-859.
- `Delete(x => !(empty.Contains(x.A) && x.A > 0))` renders `NOT (1 = 0 AND …)`. That is always true, so it deleted 3
  of 3 rows, with no exception.
- This is the scope-guard family of rules 1–3 and TASK-137: an always-false term (the empty `IN`, `1 = 0`) under a NOT
  makes a group always true. The guard checks for a missing WHERE and for reduced-away terms, but not for a NOT over a
  group that reduces to false.
- TASK-859 adds a common way to reach it, found by its review: `int? nul = null;
  Delete(x => !(x.Score > nul && x.Amount > 3))` folds to `NOT (1 = 0 AND Amount > 3)` and deleted 6 of 6 rows
  (on HEAD before TASK-859 it rendered `NOT (Score IS NULL AND …)`). The bare `!(x.Score > nul)` is refused; a
  group is not. The fold is in `DataBase.cs` (`ComparedWithNull`); the guard is `IsAlwaysTrueCondition` in
  `AbstractConnectorBase.cs`.
- Its `ComparedWithNull` doc says "a destructive statement refuses"; make it true for every shape.

## Acceptance criteria

- [ ] Reproduced per SQL provider (SQLite in-process, PostgreSQL / MySQL / SQL Server live): Delete, Update(filter,
      PropertyUpdate), Update(filter, Action) and a count with `!(alwaysFalse && x)`, `!(alwaysFalse)`, and
      `!(a && (b && alwaysFalse))`. Record rows affected.
- [ ] The condition tree is simplified with three-valued logic before the guard: `X AND FALSE` → FALSE, `NOT FALSE` →
      TRUE, `X OR TRUE` → TRUE. A destructive statement whose simplified filter is TRUE is refused by the existing
      whole-table guard (`WholeTableWriteException`), and a read returns all rows as before. The guard tests what the
      statement means, not what text it renders (rule 3).
- [ ] Regression tests per shape, destructive and read, red before the fix. Every SQL importer suite stays green.

## Out of scope

- NULL semantics (TASK-859).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

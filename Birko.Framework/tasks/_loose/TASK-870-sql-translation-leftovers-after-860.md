---
id: TASK-870
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-860]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL translation leftovers after TASK-860: case folding, decimal division, SET `.Date`

## Context

Found by TASK-860 (2026-11-08). None of these is measured yet, except where noted:
- **`ToLower` / `ToUpper` on SQLite** fold ASCII only, while C# folds Unicode. A non-ASCII name may not match.
- **Decimal `/` in a filter fragment** uses each provider's native division, not TASK-826's `ComputedDivide`, so the
  precision of the result differs by provider.
- **SET `.Date` on SQL Server** renders `DATE()`, which SQL Server rejects. It fails loudly, so no wrong rows.
- **`Replace` on SQL Server** depends on the collation.
- **SQLite integer overflow in SET** (found by TASK-860's fix): `SET Hits = Hits + 1` past `int.MaxValue` stores the
  value silently, and every later read of the row throws. That is plain arithmetic overflow, not a cast. TASK-860
  refuses narrowing SETs on SQLite for the same reason (`StoresIntegerOverflowLoudly`).
- **`ToLower` / `ToUpper` before a comparison** (TASK-869): `x.Col.ToLower() == v` and `x.Col.ToLower().Contains(..)`
  use SQL's `LOWER` / `UPPER`, which group characters differently from .NET (measured by TASK-869: 13 to 72 of 240
  cases off per provider). `StringComparison.OrdinalIgnoreCase` is now the exact form.

## Acceptance criteria

- [ ] Measure each item per provider against the C# oracle (rule 54). Record the result.
- [ ] Each one that differs is translated exactly or refused by name. An item that matches C# is recorded and closed.
- [ ] Tests per item, red before the fix.

## Out of scope

- LIKE and `StringComparison` (TASK-869).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
